using System;
using System.Collections.Generic;
using System.IO;
using Core;
using Google.Protobuf;
using Microsoft.Data.Sqlite;

namespace DataExporter
{
    internal sealed class DataExportVerifier
    {
        private readonly ProtobufRowSerializer _protobufRowSerializer;

        public DataExportVerifier(ProtobufRowSerializer protobufRowSerializer)
        {
            _protobufRowSerializer = protobufRowSerializer ?? throw new ArgumentNullException(nameof(protobufRowSerializer));
        }

        public void Verify(string databaseFilePath, IReadOnlyList<TableData> tables)
        {
            if (string.IsNullOrWhiteSpace(databaseFilePath))
            {
                throw new ArgumentException("데이터베이스 파일 경로가 비어 있습니다.", nameof(databaseFilePath));
            }

            ArgumentNullException.ThrowIfNull(tables);

            using SqliteConnection connection = new($"Data Source={databaseFilePath}");
            connection.Open();

            foreach (TableData table in tables)
            {
                VerifyTable(connection, table);
            }
        }

        private void VerifyTable(SqliteConnection connection, TableData table)
        {
            VerifyRowCount(connection, table);

            ColumnSchema primaryKeyColumn = GetPrimaryKeyColumn(table.Schema);
            List<ColumnSchema> openColumns = GetOpenColumns(table.Schema);

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = CreateSelectCommandText(table.Schema.TableName, primaryKeyColumn, openColumns);

            SqliteParameter primaryKeyParameter = command.CreateParameter();
            primaryKeyParameter.ParameterName = "$primaryKey";
            primaryKeyParameter.SqliteType = SqliteDataExporter.GetSqliteType(primaryKeyColumn.DataType);
            command.Parameters.Add(primaryKeyParameter);

            foreach (TableRow row in table.Rows)
            {
                primaryKeyParameter.Value = SqliteDataExporter.GetSqliteValue(primaryKeyColumn, row.Values[primaryKeyColumn.FieldNumber - 1]);

                using SqliteDataReader reader = command.ExecuteReader();

                if (!reader.Read())
                {
                    throw new InvalidDataException($"'{table.Schema.TableName}' 테이블에서 pk 값을 찾을 수 없습니다. Excel 행: {row.ExcelRowIndex}");
                }

                VerifyOpenColumns(reader, table, row, openColumns);
                byte[] payload = reader.GetFieldValue<byte[]>(openColumns.Count);
                IMessage expectedMessage = _protobufRowSerializer.CreateMessage(table, row);
                IMessage actualMessage = _protobufRowSerializer.Deserialize(table.Schema, payload);

                if (!expectedMessage.Equals(actualMessage))
                {
                    throw new InvalidDataException($"'{table.Schema.TableName}' 테이블의 payload 역직렬화 결과가 Excel 행과 일치하지 않습니다. Excel 행: {row.ExcelRowIndex}");
                }

                if (reader.Read())
                {
                    throw new InvalidDataException($"'{table.Schema.TableName}' 테이블에서 pk 값이 중복 조회되었습니다. Excel 행: {row.ExcelRowIndex}");
                }
            }

            VerifyIndexes(connection, table.Schema);
        }

        private static void VerifyRowCount(SqliteConnection connection, TableData table)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {SqliteDataExporter.QuoteIdentifier(table.Schema.TableName)};";
            long rowCount = Convert.ToInt64(command.ExecuteScalar());

            if (rowCount != table.Rows.Count)
            {
                throw new InvalidDataException($"'{table.Schema.TableName}' 테이블의 행 수가 Excel과 일치하지 않습니다. Excel: {table.Rows.Count}, SQLite: {rowCount}");
            }
        }

        private static void VerifyOpenColumns(SqliteDataReader reader, TableData table, TableRow row, IReadOnlyList<ColumnSchema> openColumns)
        {
            for (int columnIndex = 0; columnIndex < openColumns.Count; columnIndex++)
            {
                ColumnSchema column = openColumns[columnIndex];
                object expectedValue = SqliteDataExporter.GetSqliteValue(column, row.Values[column.FieldNumber - 1]);

                switch (column.DataType)
                {
                    case ColumnDataType.Int32:
                    case ColumnDataType.Int64:
                    case ColumnDataType.Bool:
                        if (reader.GetInt64(columnIndex) != Convert.ToInt64(expectedValue))
                        {
                            throw new InvalidDataException($"'{table.Schema.TableName}' 테이블의 op 컬럼 값이 Excel과 일치하지 않습니다. 필드: {column.Name}, Excel 행: {row.ExcelRowIndex}");
                        }

                        break;
                    case ColumnDataType.Float:
                    case ColumnDataType.Double:
                        if (!reader.GetDouble(columnIndex).Equals((double)expectedValue))
                        {
                            throw new InvalidDataException($"'{table.Schema.TableName}' 테이블의 op 컬럼 값이 Excel과 일치하지 않습니다. 필드: {column.Name}, Excel 행: {row.ExcelRowIndex}");
                        }

                        break;
                    case ColumnDataType.String:
                        if (!string.Equals(reader.GetString(columnIndex), (string)expectedValue, StringComparison.Ordinal))
                        {
                            throw new InvalidDataException($"'{table.Schema.TableName}' 테이블의 op 컬럼 값이 Excel과 일치하지 않습니다. 필드: {column.Name}, Excel 행: {row.ExcelRowIndex}");
                        }

                        break;
                    default:
                        throw new InvalidDataException($"지원하지 않는 SQLite op 컬럼 자료형입니다. 타입: {column.DataType}");
                }
            }
        }

        private static ColumnSchema GetPrimaryKeyColumn(TableSchema schema)
        {
            foreach (ColumnSchema column in schema.Columns)
            {
                if (column.IsPrimaryKey)
                {
                    return column;
                }
            }

            throw new InvalidDataException($"'{schema.TableName}' 테이블에 pk 필드가 없습니다.");
        }

        private static List<ColumnSchema> GetOpenColumns(TableSchema schema)
        {
            List<ColumnSchema> openColumns = new();

            foreach (ColumnSchema column in schema.Columns)
            {
                if (column.IsOpen)
                {
                    openColumns.Add(column);
                }
            }

            return openColumns;
        }

        private static void VerifyIndexes(SqliteConnection connection, TableSchema schema)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA index_list({SqliteDataExporter.QuoteIdentifier(schema.TableName)});";

            using SqliteDataReader reader = command.ExecuteReader();
            HashSet<string> indexNames = new(StringComparer.Ordinal);

            while (reader.Read())
            {
                indexNames.Add(reader.GetString(1));
            }

            foreach (ColumnSchema column in schema.Columns)
            {
                if (!column.IsOpen)
                {
                    continue;
                }

                string indexName = $"IX_{schema.TableName}_{column.FieldNumber}";

                if (!indexNames.Contains(indexName))
                {
                    throw new InvalidDataException($"'{schema.TableName}' 테이블에 op 컬럼 인덱스가 없습니다. 필드: {column.Name}");
                }
            }
        }

        private static string CreateSelectCommandText(string tableName, ColumnSchema primaryKeyColumn, IReadOnlyList<ColumnSchema> openColumns)
        {
            List<string> columnNames = new();

            foreach (ColumnSchema openColumn in openColumns)
            {
                columnNames.Add(SqliteDataExporter.QuoteIdentifier(openColumn.Name));
            }

            columnNames.Add(SqliteDataExporter.QuoteIdentifier(SqliteDataExporter.PAYLOAD_COLUMN_NAME));

            return $"SELECT {string.Join(", ", columnNames)} FROM {SqliteDataExporter.QuoteIdentifier(tableName)} WHERE {SqliteDataExporter.QuoteIdentifier(primaryKeyColumn.Name)} = $primaryKey;";
        }
    }
}
