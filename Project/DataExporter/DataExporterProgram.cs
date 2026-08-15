using System;
using System.Collections.Generic;
using System.IO;
using Core;

namespace DataExporter
{
    internal class DataExporterProgram
    {
        /// <summary>
        /// Excel 데이터를 읽어 Protobuf payload를 포함한 SQLite 데이터베이스를 생성하고 검증합니다.
        /// </summary>
        static void Main(string[] args)
        {
            List<TableData> tables = ReadTables();
            ProtobufRowSerializer protobufRowSerializer = new();
            SqliteDataExporter sqliteDataExporter = new(protobufRowSerializer);
            string databaseFilePath = sqliteDataExporter.Export(tables);
            DataExportVerifier dataExportVerifier = new(protobufRowSerializer);

            dataExportVerifier.Verify(databaseFilePath, tables);
            Console.WriteLine("데이터 변환 성공");
            Console.WriteLine($"- 데이터베이스: {databaseFilePath}");

            foreach (TableData table in tables)
            {
                Console.WriteLine($"- {table.Schema.TableName}: {table.Rows.Count}개 데이터 행");
            }
        }

        private static List<TableData> ReadTables()
        {
            string excelDirectoryPath = PathUtil.GetExcelDirectoryPath();
            List<string> excelFilePaths = new(Directory.GetFiles(excelDirectoryPath, "*.xlsx", SearchOption.TopDirectoryOnly));
            excelFilePaths.RemoveAll(excelFilePath => Path.GetFileName(excelFilePath).StartsWith("~$", StringComparison.Ordinal));
            excelFilePaths.Sort(StringComparer.Ordinal);

            if (excelFilePaths.Count == 0)
            {
                throw new FileNotFoundException($"Excel 파일을 찾을 수 없습니다. 경로: {excelDirectoryPath}");
            }

            ExcelReader excelReader = new();
            HashSet<string> tableNames = new(StringComparer.OrdinalIgnoreCase);
            List<TableData> tables = new();

            foreach (string excelFilePath in excelFilePaths)
            {
                WorkbookData workbookData = excelReader.Read(excelFilePath);

                foreach (TableData table in workbookData.Tables)
                {
                    if (!tableNames.Add(table.Schema.TableName))
                    {
                        throw new InvalidDataException($"테이블 이름이 중복되었습니다. 테이블: {table.Schema.TableName}");
                    }

                    tables.Add(table);
                }
            }

            return tables;
        }
    }
}
