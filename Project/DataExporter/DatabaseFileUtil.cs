using System;
using System.IO;

namespace DataExporter
{
    internal static class DatabaseFileUtil
    {
        public static void ReplaceDatabaseFile(string temporaryDatabaseFilePath, string databaseFilePath)
        {
            try
            {
                File.Move(temporaryDatabaseFilePath, databaseFilePath, true);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new IOException($"데이터베이스 파일을 교체할 수 없습니다. DB Browser 등에서 파일을 열었다면 닫은 뒤 다시 실행하세요. 파일이 읽기 전용인지도 확인하세요. 경로: {databaseFilePath}", exception);
            }
            catch (IOException exception)
            {
                throw new IOException($"데이터베이스 파일을 교체할 수 없습니다. DB Browser 등에서 파일을 열었다면 닫은 뒤 다시 실행하세요. 파일이 읽기 전용인지도 확인하세요. 경로: {databaseFilePath}", exception);
            }
        }
    }
}
