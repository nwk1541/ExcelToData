namespace SchemaGenerator
{
    internal class SchemaGeneratorProgram
    {
        static void Main(string[] args)
        {
            string rootDirectoryPath = Core.PathUtil.GetRootDirectoryPath();
            string excelDirectoryPath = Core.PathUtil.GetExcelDirectoryPath();
            string protoDirectoryPath = Core.PathUtil.GetProtoDirectoryPath();
            string outputDirectoryPath = Core.PathUtil.GetOutputDirectoryPath();

            Console.WriteLine("경로 조회 성공");
            Console.WriteLine($"작업 루트: {rootDirectoryPath}");
            Console.WriteLine($"Excel 경로: {excelDirectoryPath}");
            Console.WriteLine($"Proto 경로: {protoDirectoryPath}");
            Console.WriteLine($"Output 경로: {outputDirectoryPath}");
        }
    }
}
