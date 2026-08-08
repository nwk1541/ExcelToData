using System.Collections.Generic;

namespace Core
{
    public sealed class TableData
    {
        public TableData(TableSchema schema, IReadOnlyList<TableRow> rows)
        {
            Schema = schema;
            Rows = rows;
        }

        public TableSchema Schema { get; }
        public IReadOnlyList<TableRow> Rows { get; }
    }
}
