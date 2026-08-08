using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Core
{
    public static class WorkbookValidator
    {
        public static void Validate(WorkbookData workbookData)
        {
            ArgumentNullException.ThrowIfNull(workbookData);

            if (workbookData.Tables.Count == 0)
            {
                throw new InvalidDataException("검증할 워크시트가 없습니다.");
            }

            foreach (TableData table in workbookData.Tables)
            {
                ValidateTable(table);
            }
        }

        internal static ColumnDataType ParseDataType(string typeName, string tableName, string cellReference, out bool isRepeated)
        {
            isRepeated = false;

            if (TryParseScalarDataType(typeName, out ColumnDataType dataType))
            {
                return dataType;
            }

            const string REPEATED_PREFIX = "repeated<";

            if (typeName.StartsWith(REPEATED_PREFIX, StringComparison.Ordinal) && typeName.EndsWith(">", StringComparison.Ordinal))
            {
                string elementTypeName = typeName[REPEATED_PREFIX.Length..^1];

                if (TryParseScalarDataType(elementTypeName, out dataType))
                {
                    isRepeated = true;
                    return dataType;
                }
            }

            throw new InvalidDataException($"'{tableName}' 시트 {cellReference}: 지원하지 않는 타입 '{typeName}'입니다.");
        }

        private static void ValidateTable(TableData table)
        {
            TableSchema schema = table.Schema;

            if (!IsEnglishAlphabetOnly(schema.TableName))
            {
                throw new InvalidDataException($"'{schema.TableName}' 시트: 시트 이름은 영문자(A-Z, a-z)만 사용할 수 있습니다.");
            }

            if (schema.HeaderRowIndex < 1 || schema.StartColumnIndex < 1)
            {
                throw new InvalidDataException($"'{schema.TableName}' 시트: 헤더 위치가 올바르지 않습니다.");
            }

            if (schema.Columns.Count == 0)
            {
                throw new InvalidDataException($"'{schema.TableName}' 시트: 컬럼이 없습니다.");
            }

            HashSet<string> fieldNames = new(StringComparer.OrdinalIgnoreCase);
            ColumnSchema? primaryKeyColumn = null;
            int primaryKeyColumnIndex = -1;

            for (int columnIndex = 0; columnIndex < schema.Columns.Count; columnIndex++)
            {
                ColumnSchema column = schema.Columns[columnIndex];
                string headerCellReference = GetCellReference(column.ExcelColumnIndex, schema.HeaderRowIndex);

                if (string.IsNullOrWhiteSpace(column.Name))
                {
                    throw new InvalidDataException($"'{schema.TableName}' 시트 {headerCellReference}: 필드명이 비어 있습니다.");
                }

                if (!fieldNames.Add(column.Name))
                {
                    throw new InvalidDataException($"'{schema.TableName}' 시트 {headerCellReference}: 필드명이 중복되었습니다. 필드명: '{column.Name}'");
                }

                if (column.FieldNumber != columnIndex + 1)
                {
                    throw new InvalidDataException($"'{schema.TableName}' 시트 {headerCellReference}: Protocol Buffers 필드 번호가 열 순서와 일치하지 않습니다.");
                }

                if (column.ExcelColumnIndex != schema.StartColumnIndex + columnIndex)
                {
                    throw new InvalidDataException($"'{schema.TableName}' 시트 {headerCellReference}: 컬럼 위치가 연속되지 않았습니다.");
                }

                if (column.IsOpen && column.IsRepeated)
                {
                    throw new InvalidDataException($"'{schema.TableName}' 시트 {headerCellReference}: repeated 타입에는 op 속성을 지정할 수 없습니다.");
                }

                if (!column.IsPrimaryKey)
                {
                    continue;
                }

                if (primaryKeyColumn != null)
                {
                    throw new InvalidDataException($"'{schema.TableName}' 시트 {headerCellReference}: pk 필드는 시트마다 하나만 지정할 수 있습니다.");
                }

                if (column.IsRepeated || !IsPrimaryKeyDataType(column.DataType))
                {
                    throw new InvalidDataException($"'{schema.TableName}' 시트 {headerCellReference}: pk 필드는 int32, int64, string 단일 값 타입만 사용할 수 있습니다.");
                }

                primaryKeyColumn = column;
                primaryKeyColumnIndex = columnIndex;
            }

            if (primaryKeyColumn == null)
            {
                throw new InvalidDataException($"'{schema.TableName}' 시트: pk 필드가 없습니다.");
            }

            ValidateRows(table, primaryKeyColumn, primaryKeyColumnIndex);
        }

        private static void ValidateRows(TableData table, ColumnSchema primaryKeyColumn, int primaryKeyColumnIndex)
        {
            TableSchema schema = table.Schema;
            HashSet<string> primaryKeyValues = new(StringComparer.Ordinal);

            foreach (TableRow row in table.Rows)
            {
                if (row.Values.Count != schema.Columns.Count)
                {
                    throw new InvalidDataException($"'{schema.TableName}' 시트 {row.ExcelRowIndex}행: 데이터 열 수가 헤더 열 수와 다릅니다.");
                }

                for (int columnIndex = 0; columnIndex < schema.Columns.Count; columnIndex++)
                {
                    ValidateCellValue(schema.TableName, schema.Columns[columnIndex], row.ExcelRowIndex, row.Values[columnIndex]);
                }

                string primaryKeyValue = row.Values[primaryKeyColumnIndex];
                string primaryKeyCellReference = GetCellReference(primaryKeyColumn.ExcelColumnIndex, row.ExcelRowIndex);

                if (primaryKeyValue.Length == 0)
                {
                    throw new InvalidDataException($"'{schema.TableName}' 시트 {primaryKeyCellReference}: pk 값은 비어 있을 수 없습니다.");
                }

                string normalizedPrimaryKeyValue = NormalizePrimaryKeyValue(primaryKeyColumn.DataType, primaryKeyValue);

                if (!primaryKeyValues.Add(normalizedPrimaryKeyValue))
                {
                    throw new InvalidDataException($"'{schema.TableName}' 시트 {primaryKeyCellReference}: pk 값이 중복되었습니다. 값: '{primaryKeyValue}'");
                }
            }
        }

        private static void ValidateCellValue(string tableName, ColumnSchema column, int rowIndex, string value)
        {
            string cellReference = GetCellReference(column.ExcelColumnIndex, rowIndex);

            if (!column.IsRepeated)
            {
                ValidateScalarValue(tableName, cellReference, column.DataType, value, "값");
                return;
            }

            if (value.Length == 0)
            {
                return;
            }

            string[] elements = value.Split(',');

            for (int elementIndex = 0; elementIndex < elements.Length; elementIndex++)
            {
                string element = elements[elementIndex].Trim();
                ValidateScalarValue(tableName, cellReference, column.DataType, element, $"{elementIndex + 1}번째 목록 값");
            }
        }

        private static void ValidateScalarValue(string tableName, string cellReference, ColumnDataType dataType, string value, string valueDescription)
        {
            if (dataType == ColumnDataType.String)
            {
                return;
            }

            if (value.Length == 0)
            {
                throw new InvalidDataException($"'{tableName}' 시트 {cellReference}: {valueDescription}은(는) 비어 있을 수 없습니다.");
            }

            if (!IsScalarValueValid(dataType, value))
            {
                throw new InvalidDataException($"'{tableName}' 시트 {cellReference}: {valueDescription} '{value}'을(를) {GetDataTypeName(dataType)} 형식으로 변환할 수 없습니다.");
            }
        }

        private static bool IsScalarValueValid(ColumnDataType dataType, string value)
        {
            return dataType switch
            {
                ColumnDataType.Int32 => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
                ColumnDataType.Int64 => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
                ColumnDataType.Float => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
                ColumnDataType.Double => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
                ColumnDataType.Bool => value == "0" || value == "1",
                _ => false
            };
        }

        private static string NormalizePrimaryKeyValue(ColumnDataType dataType, string value)
        {
            return dataType switch
            {
                ColumnDataType.Int32 => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                ColumnDataType.Int64 => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                ColumnDataType.String => value,
                _ => throw new InvalidOperationException("pk 타입은 int32, int64, string만 사용할 수 있습니다.")
            };
        }

        private static bool TryParseScalarDataType(string typeName, out ColumnDataType dataType)
        {
            switch (typeName)
            {
                case "int32":
                    dataType = ColumnDataType.Int32;
                    return true;
                case "int64":
                    dataType = ColumnDataType.Int64;
                    return true;
                case "float":
                    dataType = ColumnDataType.Float;
                    return true;
                case "double":
                    dataType = ColumnDataType.Double;
                    return true;
                case "string":
                    dataType = ColumnDataType.String;
                    return true;
                case "bool":
                    dataType = ColumnDataType.Bool;
                    return true;
                default:
                    dataType = default;
                    return false;
            }
        }

        private static bool IsPrimaryKeyDataType(ColumnDataType dataType)
        {
            return dataType == ColumnDataType.Int32 || dataType == ColumnDataType.Int64 || dataType == ColumnDataType.String;
        }

        private static bool IsEnglishAlphabetOnly(string value)
        {
            if (value.Length == 0)
            {
                return false;
            }

            foreach (char character in value)
            {
                if ((character < 'A' || character > 'Z') && (character < 'a' || character > 'z'))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetDataTypeName(ColumnDataType dataType)
        {
            return dataType switch
            {
                ColumnDataType.Int32 => "int32",
                ColumnDataType.Int64 => "int64",
                ColumnDataType.Float => "float",
                ColumnDataType.Double => "double",
                ColumnDataType.String => "string",
                ColumnDataType.Bool => "bool",
                _ => throw new InvalidOperationException($"지원하지 않는 타입입니다. 타입: {dataType}")
            };
        }

        private static string GetCellReference(int columnIndex, int rowIndex)
        {
            StringBuilder columnNameBuilder = new();
            int currentColumnIndex = columnIndex;

            while (currentColumnIndex > 0)
            {
                currentColumnIndex--;
                columnNameBuilder.Insert(0, (char)('A' + currentColumnIndex % 26));
                currentColumnIndex /= 26;
            }

            return $"{columnNameBuilder}{rowIndex}";
        }
    }
}
