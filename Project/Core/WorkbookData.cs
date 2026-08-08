using System.Collections.Generic;

namespace Core
{
    public sealed class WorkbookData
    {
        public WorkbookData(IReadOnlyList<TableData> tables)
        {
            Tables = tables;
        }

        public IReadOnlyList<TableData> Tables { get; }
    }
}
