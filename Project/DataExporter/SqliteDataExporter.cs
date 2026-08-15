using System;
using System.Collections.Generic;
using System.IO;
using Core;
using Google.Protobuf;
using Microsoft.Data.Sqlite;

namespace DataExporter
{
    internal sealed class SqliteDataExporter
    {
        private const string DATABASE_FILE_NAME = "LocalData.db";
        internal const string PAYLOAD_COLUMN_NAME = "payload";

        private readonly ProtobufRowSerializer _protobufRowSerializer;

        public SqliteDataExporter(ProtobufRowSerializer protobufRowSerializer)
        {
            _protobufRowSerializer = protobufRowSerializer ?? throw new ArgumentNullException(nameof(protobufRowSerializer));
        }

        public string Export(IReadOnlyList<TableData> tables)
        {
            ArgumentNullException.ThrowIfNull(tables);

            if (tables.Count == 0)
            {
                throw new InvalidDataException("내보낼 테이블이 없습니다.");
            }

            string databaseDirectoryPath = PathUtil.GetDatabaseOutputDirectoryPath();
            string databaseFilePath = Path.Combine(databaseDirectoryPath, DATABASE_FILE_NAME);
            string temporaryDatabaseFilePath = Path.Combine(databaseDirectoryPath, $"{DATABASE_FILE_NAME}.tmp");

            Directory.CreateDirectory(databaseDirectoryPath);

            if (File.Exists(temporaryDatabaseFilePath))
            {
                File.Delete(temporaryDatabaseFilePath);
            }

            try
            {
                CreateDatabase(temporaryDatabaseFilePath, tables);
                DatabaseFileUtil.ReplaceDatabaseFile(temporaryDatabaseFilePath, databaseFilePath);
            }
            finally
            {
                if (File.Exists(temporaryDatabaseFilePath))
                {
                    File.Delete(temporaryDatabaseFilePath);
                }
            }

            return databaseFilePath;
        }

        internal static object GetSqliteValue(ColumnSchema column, string value)
        {
            // 런타임 DB에서는 repeated<T>를 payload에만 저장합니다.
            // 디버그 DB에서는 DebugSqliteDataExporter가 JSON 형식의 TEXT 컬럼으로 저장합니다.
            if (column.IsRepeated)
            {
                throw new InvalidDataException($"repeated 타입은 SQLite 독립 컬럼으로 저장할 수 없습니다. 필드: {column.Name}");
            }

            object scalarValue = ProtobufRowSerializer.ParseScalarValue(column.DataType, value);

            return column.DataType switch
            {
                ColumnDataType.Bool => (bool)scalarValue ? 1 : 0,
                ColumnDataType.Float => (double)(float)scalarValue,
                _ => scalarValue
            };
        }

        internal static SqliteType GetSqliteType(ColumnDataType dataType)
        {
            return dataType switch
            {
                ColumnDataType.Int32 => SqliteType.Integer,
                ColumnDataType.Int64 => SqliteType.Integer,
                ColumnDataType.Float => SqliteType.Real,
                ColumnDataType.Double => SqliteType.Real,
                ColumnDataType.String => SqliteType.Text,
                ColumnDataType.Bool => SqliteType.Integer,
                _ => throw new InvalidDataException($"지원하지 않는 SQLite 자료형입니다. 타입: {dataType}")
            };
        }

        internal static string QuoteIdentifier(string identifier)
        {
            return $"\"{identifier.Replace("\"", "\"\"")}\"";
        }

        private void CreateDatabase(string databaseFilePath, IReadOnlyList<TableData> tables)
        {
            SqliteConnectionStringBuilder connectionStringBuilder = new()
            {
                DataSource = databaseFilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            };

            using SqliteConnection connection = new(connectionStringBuilder.ToString());
            connection.Open();

            using SqliteTransaction transaction = connection.BeginTransaction();

            foreach (TableData table in tables)
            {
                CreateTable(connection, transaction, table.Schema);
                InsertRows(connection, transaction, table);
                CreateIndexes(connection, transaction, table.Schema);
            }

            transaction.Commit();
        }

        private static void CreateTable(SqliteConnection connection, SqliteTransaction transaction, TableSchema schema)
        {
            List<ColumnSchema> storageColumns = GetStorageColumns(schema);
            List<string> columnDefinitions = new();

            foreach (ColumnSchema column in storageColumns)
            {
                string primaryKeyClause = column.IsPrimaryKey ? " PRIMARY KEY" : string.Empty;
                columnDefinitions.Add($"{QuoteIdentifier(column.Name)} {GetSqliteDataTypeName(column.DataType)} NOT NULL{primaryKeyClause}");
            }

            columnDefinitions.Add($"{QuoteIdentifier(PAYLOAD_COLUMN_NAME)} BLOB NOT NULL");

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"CREATE TABLE {QuoteIdentifier(schema.TableName)} ({string.Join(", ", columnDefinitions)});";
            command.ExecuteNonQuery();
        }

        private void InsertRows(SqliteConnection connection, SqliteTransaction transaction, TableData table)
        {
            List<ColumnSchema> storageColumns = GetStorageColumns(table.Schema);

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = CreateInsertCommandText(table.Schema.TableName, storageColumns);

            List<SqliteParameter> storageParameters = new();

            for (int columnIndex = 0; columnIndex < storageColumns.Count; columnIndex++)
            {
                ColumnSchema column = storageColumns[columnIndex];
                SqliteParameter parameter = command.CreateParameter();
                parameter.ParameterName = $"$value{columnIndex}";
                parameter.SqliteType = GetSqliteType(column.DataType);
                command.Parameters.Add(parameter);
                storageParameters.Add(parameter);
            }

            SqliteParameter payloadParameter = command.CreateParameter();
            payloadParameter.ParameterName = "$payload";
            payloadParameter.SqliteType = SqliteType.Blob;
            command.Parameters.Add(payloadParameter);
            command.Prepare();

            foreach (TableRow row in table.Rows)
            {
                for (int columnIndex = 0; columnIndex < storageColumns.Count; columnIndex++)
                {
                    ColumnSchema column = storageColumns[columnIndex];
                    storageParameters[columnIndex].Value = GetSqliteValue(column, row.Values[column.FieldNumber - 1]);
                }

                IMessage message = _protobufRowSerializer.CreateMessage(table, row);
                payloadParameter.Value = message.ToByteArray();
                command.ExecuteNonQuery();
            }
        }

        private static void CreateIndexes(SqliteConnection connection, SqliteTransaction transaction, TableSchema schema)
        {
            foreach (ColumnSchema column in schema.Columns)
            {
                if (!column.IsOpen)
                {
                    continue;
                }

                string indexName = $"IX_{schema.TableName}_{column.FieldNumber}";

                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"CREATE INDEX {QuoteIdentifier(indexName)} ON {QuoteIdentifier(schema.TableName)} ({QuoteIdentifier(column.Name)});";
                command.ExecuteNonQuery();
            }
        }

        private static List<ColumnSchema> GetStorageColumns(TableSchema schema)
        {
            List<ColumnSchema> storageColumns = new();

            foreach (ColumnSchema column in schema.Columns)
            {
                if (column.IsPrimaryKey || column.IsOpen)
                {
                    storageColumns.Add(column);
                }
            }

            return storageColumns;
        }

        private static string CreateInsertCommandText(string tableName, IReadOnlyList<ColumnSchema> storageColumns)
        {
            List<string> columnNames = new();
            List<string> parameterNames = new();

            for (int columnIndex = 0; columnIndex < storageColumns.Count; columnIndex++)
            {
                columnNames.Add(QuoteIdentifier(storageColumns[columnIndex].Name));
                parameterNames.Add($"$value{columnIndex}");
            }

            columnNames.Add(QuoteIdentifier(PAYLOAD_COLUMN_NAME));
            parameterNames.Add("$payload");

            return $"INSERT INTO {QuoteIdentifier(tableName)} ({string.Join(", ", columnNames)}) VALUES ({string.Join(", ", parameterNames)});";
        }

        internal static string GetSqliteDataTypeName(ColumnDataType dataType)
        {
            return dataType switch
            {
                ColumnDataType.Int32 => "INTEGER",
                ColumnDataType.Int64 => "INTEGER",
                ColumnDataType.Float => "REAL",
                ColumnDataType.Double => "REAL",
                ColumnDataType.String => "TEXT",
                ColumnDataType.Bool => "INTEGER",
                _ => throw new InvalidDataException($"지원하지 않는 SQLite 자료형입니다. 타입: {dataType}")
            };
        }
    }
}
