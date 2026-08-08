using System.Collections.Generic;

namespace Core
{
    public sealed class TableRow
    {
        public TableRow(int excelRowIndex, IReadOnlyList<string> values)
        {
            ExcelRowIndex = excelRowIndex;
            Values = values;
        }

        public int ExcelRowIndex { get; }

        public IReadOnlyList<string> Values { get; }
    }
}
