using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using Core;
using Microsoft.Data.Sqlite;

namespace DataExporter
{
    internal sealed class DebugSqliteDataExporter
    {
        private const string DATABASE_FILE_NAME = "LocalData.db";

        private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public string Export(IReadOnlyList<TableData> tables)
        {
            ArgumentNullException.ThrowIfNull(tables);

            if (tables.Count == 0)
            {
                throw new InvalidDataException("내보낼 테이블이 없습니다.");
            }

            string databaseDirectoryPath = PathUtil.GetDebugDatabaseOutputDirectoryPath();
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

        internal static object GetDebugSqliteValue(ColumnSchema column, string value)
        {
            if (!column.IsRepeated)
            {
                return SqliteDataExporter.GetSqliteValue(column, value);
            }

            return GetRepeatedJsonValue(column, value);
        }

        internal static SqliteType GetDebugSqliteType(ColumnSchema column)
        {
            if (column.IsRepeated)
            {
                return SqliteType.Text;
            }

            return SqliteDataExporter.GetSqliteType(column.DataType);
        }

        private static void CreateDatabase(string databaseFilePath, IReadOnlyList<TableData> tables)
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
            }

            transaction.Commit();
        }

        private static void CreateTable(SqliteConnection connection, SqliteTransaction transaction, TableSchema schema)
        {
            List<string> columnDefinitions = new();

            foreach (ColumnSchema column in schema.Columns)
            {
                string primaryKeyClause = column.IsPrimaryKey ? " PRIMARY KEY" : string.Empty;
                string sqliteDataTypeName = column.IsRepeated ? "TEXT" : SqliteDataExporter.GetSqliteDataTypeName(column.DataType);
                columnDefinitions.Add($"{SqliteDataExporter.QuoteIdentifier(column.Name)} {sqliteDataTypeName} NOT NULL{primaryKeyClause}");
            }

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"CREATE TABLE {SqliteDataExporter.QuoteIdentifier(schema.TableName)} ({string.Join(", ", columnDefinitions)});";
            command.ExecuteNonQuery();
        }

        private static void InsertRows(SqliteConnection connection, SqliteTransaction transaction, TableData table)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = CreateInsertCommandText(table.Schema.TableName, table.Schema.Columns);

            List<SqliteParameter> parameters = new();

            for (int columnIndex = 0; columnIndex < table.Schema.Columns.Count; columnIndex++)
            {
                ColumnSchema column = table.Schema.Columns[columnIndex];
                SqliteParameter parameter = command.CreateParameter();
                parameter.ParameterName = $"$value{columnIndex}";
                parameter.SqliteType = GetDebugSqliteType(column);
                command.Parameters.Add(parameter);
                parameters.Add(parameter);
            }

            command.Prepare();

            foreach (TableRow row in table.Rows)
            {
                for (int columnIndex = 0; columnIndex < table.Schema.Columns.Count; columnIndex++)
                {
                    ColumnSchema column = table.Schema.Columns[columnIndex];
                    parameters[columnIndex].Value = GetDebugSqliteValue(column, row.Values[column.FieldNumber - 1]);
                }

                command.ExecuteNonQuery();
            }
        }

        private static string CreateInsertCommandText(string tableName, IReadOnlyList<ColumnSchema> columns)
        {
            List<string> columnNames = new();
            List<string> parameterNames = new();

            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                columnNames.Add(SqliteDataExporter.QuoteIdentifier(columns[columnIndex].Name));
                parameterNames.Add($"$value{columnIndex}");
            }

            return $"INSERT INTO {SqliteDataExporter.QuoteIdentifier(tableName)} ({string.Join(", ", columnNames)}) VALUES ({string.Join(", ", parameterNames)});";
        }

        private static string GetRepeatedJsonValue(ColumnSchema column, string value)
        {
            List<object> values = new();

            if (value.Length > 0)
            {
                string[] items = value.Split(',');

                foreach (string item in items)
                {
                    values.Add(ProtobufRowSerializer.ParseScalarValue(column.DataType, item.Trim()));
                }
            }

            return JsonSerializer.Serialize(values, _jsonSerializerOptions);
        }
    }
}
