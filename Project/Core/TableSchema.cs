using System.Collections.Generic;

namespace Core
{
    public sealed class TableSchema
    {
        public TableSchema(string tableName, int headerRowIndex, int startColumnIndex, IReadOnlyList<ColumnSchema> columns)
        {
            TableName = tableName;
            HeaderRowIndex = headerRowIndex;
            StartColumnIndex = startColumnIndex;
            Columns = columns;
        }

        public string TableName { get; }
        public int HeaderRowIndex { get; }
        public int StartColumnIndex { get; }

        public IReadOnlyList<ColumnSchema> Columns { get; }
    }
}
