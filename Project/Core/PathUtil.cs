using System;
using System.IO;

namespace Core
{
    public static class PathUtil
    {
        public const string EXCEL_DIRECTORY_NAME = "Excel";
        public const string PROTO_DIRECTORY_NAME = "Proto";
        public const string OUTPUT_DIRECTORY_NAME = "Output";
        public const string CSHARP_OUTPUT_DIRECTORY_NAME = "CSharp";
        public const string DATABASE_OUTPUT_DIRECTORY_NAME = "Database";

        private const string PROJECT_DIRECTORY_NAME = "Project";
        private const string SOLUTION_FILE_NAME = "Project.slnx";

        public static string GetRootDirectoryPath()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null && !IsRootDirectory(directory))
            {
                directory = directory.Parent;
            }

            if (directory == null)
            {
                throw new DirectoryNotFoundException($"작업 루트를 찾지 못했습니다. 실행 위치: {AppContext.BaseDirectory}");
            }

            return directory.FullName;
        }

        private static bool IsRootDirectory(DirectoryInfo directory)
        {
            string excelDirectoryPath = Path.Combine(directory.FullName, EXCEL_DIRECTORY_NAME);
            string solutionFilePath = Path.Combine(directory.FullName, PROJECT_DIRECTORY_NAME, SOLUTION_FILE_NAME);

            return Directory.Exists(excelDirectoryPath) && File.Exists(solutionFilePath);
        }

        public static string GetExcelDirectoryPath()
        {
            return Path.Combine(GetRootDirectoryPath(), EXCEL_DIRECTORY_NAME);
        }

        public static string GetProtoDirectoryPath()
        {
            return Path.Combine(GetRootDirectoryPath(), PROTO_DIRECTORY_NAME);
        }

        public static string GetOutputDirectoryPath()
        {
            return Path.Combine(GetRootDirectoryPath(), OUTPUT_DIRECTORY_NAME);
        }

        public static string GetCSharpOutputDirectoryPath()
        {
            return Path.Combine(GetOutputDirectoryPath(), CSHARP_OUTPUT_DIRECTORY_NAME);
        }

        public static string GetDatabaseOutputDirectoryPath()
        {
            return Path.Combine(GetOutputDirectoryPath(), DATABASE_OUTPUT_DIRECTORY_NAME);
        }
    }
}
