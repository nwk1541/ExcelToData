using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using Core;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace DataExporter
{
    internal sealed class ProtobufRowSerializer
    {
        private const string MESSAGE_NAMESPACE_NAME = "Data.Local";

        public IMessage CreateMessage(TableData table, TableRow row)
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(row);

            if (row.Values.Count != table.Schema.Columns.Count)
            {
                throw new InvalidDataException($"'{table.Schema.TableName}' 시트 {row.ExcelRowIndex}행: 데이터 열 수가 헤더 열 수와 다릅니다.");
            }

            IMessage message = CreateMessageInstance(table.Schema);

            for (int columnIndex = 0; columnIndex < table.Schema.Columns.Count; columnIndex++)
            {
                ColumnSchema column = table.Schema.Columns[columnIndex];
                FieldDescriptor field = GetFieldDescriptor(message, table.Schema.TableName, column);
                string value = row.Values[columnIndex];

                if (column.IsRepeated)
                {
                    SetRepeatedFieldValue(message, field, column, value);
                    continue;
                }

                field.Accessor.SetValue(message, ParseScalarValue(column.DataType, value));
            }

            return message;
        }

        public IMessage Deserialize(TableSchema schema, byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentNullException.ThrowIfNull(payload);

            IMessage message = CreateMessageInstance(schema);

            return message.Descriptor.Parser.ParseFrom(payload);
        }

        internal static object ParseScalarValue(ColumnDataType dataType, string value)
        {
            return dataType switch
            {
                ColumnDataType.Int32 => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
                ColumnDataType.Int64 => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
                ColumnDataType.Float => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture),
                ColumnDataType.Double => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture),
                ColumnDataType.String => value,
                ColumnDataType.Bool => ParseBoolValue(value),
                _ => throw new InvalidDataException($"지원하지 않는 자료형입니다. 타입: {dataType}")
            };
        }

        private static IMessage CreateMessageInstance(TableSchema schema)
        {
            string messageTypeName = $"{MESSAGE_NAMESPACE_NAME}.{schema.TableName}";
            Type? messageType = Assembly.GetExecutingAssembly().GetType(messageTypeName, false, false);

            if (messageType == null || !typeof(IMessage).IsAssignableFrom(messageType))
            {
                throw new InvalidDataException($"'{schema.TableName}' 테이블에 대응하는 생성 C# 메시지를 찾을 수 없습니다. SchemaGenerator를 먼저 실행했는지 확인하세요. 메시지: {messageTypeName}");
            }

            if (Activator.CreateInstance(messageType) is not IMessage message)
            {
                throw new InvalidDataException($"'{schema.TableName}' 테이블의 Protocol Buffers 메시지를 생성할 수 없습니다. 메시지: {messageTypeName}");
            }

            return message;
        }

        private static FieldDescriptor GetFieldDescriptor(IMessage message, string tableName, ColumnSchema column)
        {
            FieldDescriptor? field = message.Descriptor.FindFieldByNumber(column.FieldNumber);

            if (field == null || field.Name != column.Name || field.IsRepeated != column.IsRepeated || field.FieldType != GetFieldType(column.DataType))
            {
                throw new InvalidDataException($"'{tableName}' 테이블의 생성 C# 메시지가 현재 Excel 규약과 일치하지 않습니다. SchemaGenerator를 다시 실행하세요. 필드: {column.Name}");
            }

            return field;
        }

        private static void SetRepeatedFieldValue(IMessage message, FieldDescriptor field, ColumnSchema column, string value)
        {
            if (value.Length == 0)
            {
                return;
            }

            if (field.Accessor.GetValue(message) is not IList repeatedValues)
            {
                throw new InvalidDataException($"'{column.Name}' 필드의 repeated 값을 설정할 수 없습니다.");
            }

            string[] values = value.Split(',');

            foreach (string item in values)
            {
                repeatedValues.Add(ParseScalarValue(column.DataType, item.Trim()));
            }
        }

        private static bool ParseBoolValue(string value)
        {
            return value switch
            {
                "0" => false,
                "1" => true,
                _ => throw new InvalidDataException($"bool 값은 0 또는 1만 사용할 수 있습니다. 값: '{value}'")
            };
        }

        private static FieldType GetFieldType(ColumnDataType dataType)
        {
            return dataType switch
            {
                ColumnDataType.Int32 => FieldType.Int32,
                ColumnDataType.Int64 => FieldType.Int64,
                ColumnDataType.Float => FieldType.Float,
                ColumnDataType.Double => FieldType.Double,
                ColumnDataType.String => FieldType.String,
                ColumnDataType.Bool => FieldType.Bool,
                _ => throw new InvalidDataException($"지원하지 않는 Protocol Buffers 자료형입니다. 타입: {dataType}")
            };
        }
    }
}
