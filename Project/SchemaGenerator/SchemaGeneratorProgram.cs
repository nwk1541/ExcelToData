using System.IO;
using Core;

namespace SchemaGenerator
{
    internal class SchemaGeneratorProgram
    {
        static void Main(string[] args)
        {
            string excelFilePath = Path.Combine(PathUtil.GetExcelDirectoryPath(), "캐릭터_정보.xlsx");
            WorkbookData workbookData = new ExcelReader().Read(excelFilePath);

            Console.WriteLine($"규약 검증 성공: {Path.GetFileName(excelFilePath)}");

            foreach (TableData table in workbookData.Tables)
            {
                Console.WriteLine($"- {table.Schema.TableName}: {table.Schema.Columns.Count}개 컬럼, {table.Rows.Count}개 데이터 행");
            }
        }
    }
}
