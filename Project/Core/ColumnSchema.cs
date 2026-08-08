namespace Core
{
    public sealed class ColumnSchema
    {
        public ColumnSchema(string name, ColumnDataType dataType, bool isRepeated, bool isPrimaryKey, bool isOpen, int fieldNumber, int excelColumnIndex)
        {
            Name = name;
            DataType = dataType;
            IsRepeated = isRepeated;
            IsPrimaryKey = isPrimaryKey;
            IsOpen = isOpen;
            FieldNumber = fieldNumber;
            ExcelColumnIndex = excelColumnIndex;
        }

        public string Name { get; }
        public ColumnDataType DataType { get; }
        public bool IsRepeated { get; }
        public bool IsPrimaryKey { get; }
        public bool IsOpen { get; }
        public int FieldNumber { get; }
        public int ExcelColumnIndex { get; }
    }
}
