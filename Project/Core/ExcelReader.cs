using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Core
{
    public sealed class ExcelReader
    {
        private const string RANGE_DECLARATION_NAME = "#Range";
        private const string TYPE_DECLARATION_NAME = "#Type";

        public WorkbookData Read(string workbookPath)
        {
            if (string.IsNullOrWhiteSpace(workbookPath))
            {
                throw new ArgumentException("Excel 파일 경로가 비어 있습니다.", nameof(workbookPath));
            }

            if (!File.Exists(workbookPath))
            {
                throw new FileNotFoundException("Excel 파일을 찾을 수 없습니다.", workbookPath);
            }

            using SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.Open(workbookPath, false);
            WorkbookPart? workbookPart = spreadsheetDocument.WorkbookPart;

            if (workbookPart == null)
            {
                throw new InvalidDataException("Excel 파일에 WorkbookPart가 없습니다.");
            }

            Workbook? workbook = workbookPart.Workbook;

            if (workbook == null)
            {
                throw new InvalidDataException("Excel 파일에 Workbook이 없습니다.");
            }

            Sheets? sheets = workbook.Sheets;

            if (sheets == null)
            {
                throw new InvalidDataException("Excel 파일에 워크시트가 없습니다.");
            }

            List<TableData> tables = new();

            foreach (Sheet sheet in sheets.Elements<Sheet>())
            {
                tables.Add(ReadTableData(workbookPart, sheet));
            }

            WorkbookData workbookData = new(tables);
            WorkbookValidator.Validate(workbookData);

            return workbookData;
        }

        private static TableData ReadTableData(WorkbookPart workbookPart, Sheet sheet)
        {
            string sheetName = sheet.Name?.Value ?? throw new InvalidDataException("이름이 없는 워크시트가 있습니다.");
            string relationshipId = sheet.Id?.Value ?? throw new InvalidDataException($"'{sheetName}' 시트의 관계 ID가 없습니다.");

            if (workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart)
            {
                throw new InvalidDataException($"'{sheetName}' 시트는 일반 워크시트가 아닙니다.");
            }

            Dictionary<CellPosition, CellContent> cellContents = ReadCellContents(worksheetPart, workbookPart.SharedStringTablePart?.SharedStringTable, sheetName);
            WorksheetCell rangeDeclaration = FindDeclaration(cellContents, RANGE_DECLARATION_NAME, sheetName);
            CellRange range = ParseRangeDeclaration(rangeDeclaration, sheetName);
            WorksheetCell typeDeclaration = FindDeclaration(cellContents, TYPE_DECLARATION_NAME, sheetName);
            CellPosition typeStartPosition = ParseTypeDeclaration(typeDeclaration, sheetName);

            EnsureDeclarationIsOutsideRange(rangeDeclaration, range, sheetName);
            EnsureDeclarationIsOutsideRange(typeDeclaration, range, sheetName);

            if (typeStartPosition.ColumnIndex != range.Start.ColumnIndex || typeStartPosition.RowIndex != range.Start.RowIndex + 1)
            {
                throw new InvalidDataException($"'{sheetName}' 시트 {GetCellReference(typeDeclaration.Position)}: #Type은 헤더 바로 다음 행의 첫 셀을 지정해야 합니다.");
            }

            TableSchema schema = ReadTableSchema(cellContents, sheetName, range);
            List<TableRow> rows = ReadTableRows(cellContents, sheetName, range);

            return new TableData(schema, rows);
        }

        private static Dictionary<CellPosition, CellContent> ReadCellContents(WorksheetPart worksheetPart, SharedStringTable? sharedStringTable, string sheetName)
        {
            Worksheet? worksheet = worksheetPart.Worksheet;

            if (worksheet == null)
            {
                throw new InvalidDataException($"'{sheetName}' 시트에 Worksheet가 없습니다.");
            }

            Dictionary<CellPosition, CellContent> cellContents = new();

            foreach (Cell cell in worksheet.Descendants<Cell>())
            {
                string cellReference = cell.CellReference?.Value ?? throw new InvalidDataException($"'{sheetName}' 시트에 셀 참조가 없는 셀이 있습니다.");

                if (!TryParseCellPosition(cellReference, out CellPosition position))
                {
                    throw new InvalidDataException($"'{sheetName}' 시트: 올바르지 않은 셀 참조입니다. 셀: '{cellReference}'");
                }

                if (!cellContents.TryAdd(position, ReadCellContent(cell, sharedStringTable, sheetName, position)))
                {
                    throw new InvalidDataException($"'{sheetName}' 시트 {cellReference}: 중복된 셀 참조입니다.");
                }
            }

            return cellContents;
        }

        private static CellContent ReadCellContent(Cell cell, SharedStringTable? sharedStringTable, string sheetName, CellPosition position)
        {
            if (cell.CellFormula != null && cell.CellValue == null)
            {
                return new CellContent(string.Empty, true);
            }

            if (cell.DataType?.Value == CellValues.SharedString)
            {
                string? sharedStringIndexText = cell.CellValue?.Text;

                if (!int.TryParse(sharedStringIndexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sharedStringIndex) || sharedStringTable == null || sharedStringIndex < 0 || sharedStringIndex >= sharedStringTable.ChildElements.Count)
                {
                    throw new InvalidDataException($"'{sheetName}' 시트 {GetCellReference(position)}: 올바르지 않은 공유 문자열 참조입니다.");
                }

                return new CellContent(sharedStringTable.ChildElements[sharedStringIndex].InnerText, false);
            }

            if (cell.DataType?.Value == CellValues.InlineString)
            {
                return new CellContent(cell.InlineString?.InnerText ?? string.Empty, false);
            }

            return new CellContent(cell.CellValue?.Text ?? string.Empty, false);
        }

        private static WorksheetCell FindDeclaration(Dictionary<CellPosition, CellContent> cellContents, string declarationName, string sheetName)
        {
            WorksheetCell? declaration = null;

            foreach (KeyValuePair<CellPosition, CellContent> cellContentPair in cellContents)
            {
                string value = cellContentPair.Value.Value;

                if (!value.StartsWith(declarationName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (declaration != null)
                {
                    throw new InvalidDataException($"'{sheetName}' 시트: {declarationName} 선언은 정확히 하나만 지정할 수 있습니다.");
                }

                declaration = new WorksheetCell(cellContentPair.Key, value);
            }

            if (declaration == null)
            {
                throw new InvalidDataException($"'{sheetName}' 시트: {declarationName} 선언이 없습니다.");
            }

            WorksheetCell result = declaration.Value;

            if (!result.Value.StartsWith($"{declarationName}=", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"'{sheetName}' 시트 {GetCellReference(result.Position)}: {declarationName} 선언 형식이 올바르지 않습니다.");
            }

            return result;
        }

        private static CellRange ParseRangeDeclaration(WorksheetCell declaration, string sheetName)
        {
            string rangeText = declaration.Value[(RANGE_DECLARATION_NAME.Length + 1)..];
            string[] rangeParts = rangeText.Split(':');

            if (rangeParts.Length != 2 || !TryParseCellPosition(rangeParts[0], out CellPosition startPosition) || !TryParseCellPosition(rangeParts[1], out CellPosition endPosition))
            {
                throw new InvalidDataException($"'{sheetName}' 시트 {GetCellReference(declaration.Position)}: #Range 형식이 올바르지 않습니다.");
            }

            if (startPosition.ColumnIndex > endPosition.ColumnIndex || startPosition.RowIndex > endPosition.RowIndex)
            {
                throw new InvalidDataException($"'{sheetName}' 시트 {GetCellReference(declaration.Position)}: #Range의 시작 셀은 끝 셀보다 앞에 있어야 합니다.");
            }

            if (endPosition.RowIndex - startPosition.RowIndex < 2)
            {
                throw new InvalidDataException($"'{sheetName}' 시트 {GetCellReference(declaration.Position)}: #Range에는 헤더, 타입, 데이터 행을 모두 포함해야 합니다.");
            }

            return new CellRange(startPosition, endPosition);
        }

        private static CellPosition ParseTypeDeclaration(WorksheetCell declaration, string sheetName)
        {
            string typeStartCellReference = declaration.Value[(TYPE_DECLARATION_NAME.Length + 1)..];

            if (!TryParseCellPosition(typeStartCellReference, out CellPosition typeStartPosition))
            {
                throw new InvalidDataException($"'{sheetName}' 시트 {GetCellReference(declaration.Position)}: #Type 형식이 올바르지 않습니다.");
            }

            return typeStartPosition;
        }

        private static void EnsureDeclarationIsOutsideRange(WorksheetCell declaration, CellRange range, string sheetName)
        {
            if (range.Contains(declaration.Position))
            {
                throw new InvalidDataException($"'{sheetName}' 시트 {GetCellReference(declaration.Position)}: 선언 셀은 #Range 밖에 있어야 합니다.");
            }
        }

        private static TableSchema ReadTableSchema(Dictionary<CellPosition, CellContent> cellContents, string sheetName, CellRange range)
        {
            List<ColumnSchema> columns = new();

            for (int columnIndex = range.Start.ColumnIndex; columnIndex <= range.End.ColumnIndex; columnIndex++)
            {
                CellPosition headerPosition = new(columnIndex, range.Start.RowIndex);
                CellPosition typePosition = new(columnIndex, range.Start.RowIndex + 1);
                string header = GetCellValue(cellContents, headerPosition, sheetName);
                string typeName = GetCellValue(cellContents, typePosition, sheetName);

                if (header.Length == 0)
                {
                    throw new InvalidDataException($"'{sheetName}' 시트 {GetCellReference(headerPosition)}: 헤더가 비어 있습니다.");
                }

                if (typeName.Length == 0)
                {
                    throw new InvalidDataException($"'{sheetName}' 시트 {GetCellReference(typePosition)}: 타입이 비어 있습니다.");
                }

                ParseHeader(header, sheetName, headerPosition, out string fieldName, out bool isPrimaryKey, out bool isOpen);
                ColumnDataType dataType = WorkbookValidator.ParseDataType(typeName, sheetName, GetCellReference(typePosition), out bool isRepeated);
                int fieldNumber = columnIndex - range.Start.ColumnIndex + 1;

                columns.Add(new ColumnSchema(fieldName, dataType, isRepeated, isPrimaryKey, isOpen, fieldNumber, columnIndex));
            }

            return new TableSchema(sheetName, range.Start.RowIndex, range.Start.ColumnIndex, columns);
        }

        private static List<TableRow> ReadTableRows(Dictionary<CellPosition, CellContent> cellContents, string sheetName, CellRange range)
        {
            List<TableRow> rows = new();

            for (int rowIndex = range.Start.RowIndex + 2; rowIndex <= range.End.RowIndex; rowIndex++)
            {
                List<string> values = new();

                for (int columnIndex = range.Start.ColumnIndex; columnIndex <= range.End.ColumnIndex; columnIndex++)
                {
                    values.Add(GetCellValue(cellContents, new CellPosition(columnIndex, rowIndex), sheetName));
                }

                rows.Add(new TableRow(rowIndex, values));
            }

            return rows;
        }

        private static string GetCellValue(Dictionary<CellPosition, CellContent> cellContents, CellPosition position, string sheetName)
        {
            if (!cellContents.TryGetValue(position, out CellContent cellContent))
            {
                return string.Empty;
            }

            if (cellContent.HasFormulaWithoutCachedValue)
            {
                throw new InvalidDataException($"'{sheetName}' 시트 {GetCellReference(position)}: 수식 셀에 저장된 계산값이 없습니다.");
            }

            return cellContent.Value;
        }

        private static void ParseHeader(string header, string sheetName, CellPosition position, out string fieldName, out bool isPrimaryKey, out bool isOpen)
        {
            string[] headerParts = header.Split(':');
            fieldName = headerParts[0];
            isPrimaryKey = false;
            isOpen = false;

            if (string.IsNullOrWhiteSpace(fieldName))
            {
                throw new InvalidDataException($"'{sheetName}' 시트 {GetCellReference(position)}: 필드명이 비어 있습니다.");
            }

            if (headerParts.Length == 1)
            {
                return;
            }

            if (headerParts.Length != 2)
            {
                throw new InvalidDataException($"'{sheetName}' 시트 {GetCellReference(position)}: 헤더 속성 형식이 올바르지 않습니다.");
            }

            switch (headerParts[1])
            {
                case "pk":
                    isPrimaryKey = true;
                    return;
                case "op":
                    isOpen = true;
                    return;
                default:
                    throw new InvalidDataException($"'{sheetName}' 시트 {GetCellReference(position)}: 지원하지 않는 헤더 속성 '{headerParts[1]}'입니다.");
            }
        }

        private static bool TryParseCellPosition(string cellReference, out CellPosition position)
        {
            position = default;

            if (cellReference.Length == 0)
            {
                return false;
            }

            int columnCharacterCount = 0;

            while (columnCharacterCount < cellReference.Length && IsEnglishAlphabet(cellReference[columnCharacterCount]))
            {
                columnCharacterCount++;
            }

            if (columnCharacterCount == 0 || columnCharacterCount == cellReference.Length)
            {
                return false;
            }

            int columnIndex = 0;

            for (int characterIndex = 0; characterIndex < columnCharacterCount; characterIndex++)
            {
                int characterValue = char.ToUpperInvariant(cellReference[characterIndex]) - 'A' + 1;

                if (columnIndex > (int.MaxValue - characterValue) / 26)
                {
                    return false;
                }

                columnIndex = columnIndex * 26 + characterValue;
            }

            for (int characterIndex = columnCharacterCount; characterIndex < cellReference.Length; characterIndex++)
            {
                if (cellReference[characterIndex] < '0' || cellReference[characterIndex] > '9')
                {
                    return false;
                }
            }

            if (!int.TryParse(cellReference[columnCharacterCount..], NumberStyles.None, CultureInfo.InvariantCulture, out int rowIndex) || rowIndex < 1)
            {
                return false;
            }

            position = new CellPosition(columnIndex, rowIndex);
            return true;
        }

        private static bool IsEnglishAlphabet(char value)
        {
            return value >= 'A' && value <= 'Z' || value >= 'a' && value <= 'z';
        }

        private static string GetCellReference(CellPosition position)
        {
            StringBuilder columnNameBuilder = new();
            int currentColumnIndex = position.ColumnIndex;

            while (currentColumnIndex > 0)
            {
                currentColumnIndex--;
                columnNameBuilder.Insert(0, (char)('A' + currentColumnIndex % 26));
                currentColumnIndex /= 26;
            }

            return $"{columnNameBuilder}{position.RowIndex}";
        }

        private readonly record struct CellPosition(int ColumnIndex, int RowIndex);

        private readonly record struct CellRange(CellPosition Start, CellPosition End)
        {
            public bool Contains(CellPosition position)
            {
                return position.ColumnIndex >= Start.ColumnIndex && position.ColumnIndex <= End.ColumnIndex && position.RowIndex >= Start.RowIndex && position.RowIndex <= End.RowIndex;
            }
        }

        private readonly record struct CellContent(string Value, bool HasFormulaWithoutCachedValue);

        private readonly record struct WorksheetCell(CellPosition Position, string Value);
    }
}
