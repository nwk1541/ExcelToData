using System;
using System.IO;
using System.Linq;
using System.Text;
using Data.Local;
using Google.Protobuf;
using Microsoft.Data.Sqlite;
using UnityEngine;

namespace Voltage.Sample
{
    /// <summary>
    /// SampleScene에서 동일하게 실행할 에디터·Windows Player용 SQLite 검증입니다.
    /// 프로젝트의 DataManager와 패키지의 향후 SqlManager 구현에 포함하지 않는 검증 전용 코드입니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SqliteValidation : MonoBehaviour
    {
        [SerializeField] private DataManager _dataManager;
        [SerializeField, TextArea(4, 16)] private string _lastResult;

        public bool LastValidationPassed { get; private set; }
        public string LastResult => _lastResult;

        private void Start()
        {
            RunValidation();
        }

        [ContextMenu("Run SQLite Validation")]
        public void RunValidation()
        {
            LastValidationPassed = false;
            StringBuilder result = new();

            try
            {
                Require(Application.isPlaying, "Play 모드에서 검증을 실행해야 합니다.");
                Require(_dataManager != null, "Inspector에서 DataManager를 연결해야 합니다.");

                if (!_dataManager.IsInitialized)
                {
                    _dataManager.Initialize();
                }

                result.AppendLine($"DB 경로: {_dataManager.DatabasePath}");
                ValidateDatabase(_dataManager.DatabasePath, result);
                LastValidationPassed = true;
                _lastResult = result.ToString();
                Debug.Log($"[SQLite 검증 성공]\n{_lastResult}", this);
            }
            catch (Exception exception)
            {
                result.AppendLine($"실패: {exception}");
                _lastResult = result.ToString();
                Debug.LogError($"[SQLite 검증 실패]\n{_lastResult}", this);
            }
        }

        private static void ValidateDatabase(string databasePath, StringBuilder result)
        {
            // Windows 네이티브 DLL을 호출하는 provider를 명시적으로 선택합니다.
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3());

            string directory = Path.Combine(Application.temporaryCachePath, "VoltageSqliteValidation", Guid.NewGuid().ToString("N"));
            string validationPath = Path.Combine(directory, "LocalData.db");
            string replacementPath = Path.Combine(directory, "Replacement.db");
            byte[] originalBytes = File.ReadAllBytes(databasePath);
            Directory.CreateDirectory(directory);

            try
            {
                File.Copy(databasePath, validationPath);

                using (SqliteConnection connection = OpenReadOnly(validationPath))
                {
                    result.AppendLine($"PASS: SQLite {connection.ServerVersion} 연결 ({Application.platform})");
                    ValidateRows(connection);
                    result.AppendLine("PASS: CharTable·StatTable 각 3행의 SQL 키와 Protobuf 전체 필드 일치");
                    ValidateParameters(connection);
                    result.AppendLine("PASS: 기본 키 조회·결과 없음·type 복수 조회·한글 문자열과 BLOB 바인딩");
                }

                VerifyReplacement(databasePath, validationPath, replacementPath);
                result.AppendLine("PASS: 정상 조회 후 연결 해제·DB 복사본 교체·재연결");
                ValidateFailureCleanup(validationPath);
                VerifyReplacement(databasePath, validationPath, replacementPath);
                result.AppendLine("PASS: SQL 오류 이후 연결 해제·DB 복사본 교체·재연결");
                Require(originalBytes.SequenceEqual(File.ReadAllBytes(databasePath)), "프로젝트 DB의 내용이 변경되었습니다.");
                result.AppendLine("PASS: 프로젝트 DB 내용 유지");
            }
            finally
            {
                // 이번 실행이 생성한 파일만 정리합니다. 프로젝트 DB와 배포 원본은 삭제하지 않습니다.
                File.Delete(replacementPath);
                File.Delete(validationPath);
                Directory.Delete(directory);
            }
        }

        private static SqliteConnection OpenReadOnly(string databasePath)
        {
            SqliteConnectionStringBuilder builder = new()
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };

            SqliteConnection connection = new(builder.ToString());
            try
            {
                connection.Open();
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        private static void ValidateRows(SqliteConnection connection)
        {
            // Excel/캐릭터_정보.xlsx의 샘플 값입니다. 샘플 데이터를 바꾸면 기대값도 함께 갱신합니다.
            CharTable[] expectedCharacters =
            {
                new() { Id = 1, Type = 1, StatId = 1, SkillIds = { 1 }, Name = "플레이어블1" },
                new() { Id = 2, Type = 1, StatId = 2, SkillIds = { 1, 2 }, Name = "플레이어블2" },
                new() { Id = 3, Type = 2, StatId = 3, SkillIds = { 1, 2, 3 }, Name = "몬스터1" }
            };

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, type, payload FROM CharTable ORDER BY id";
                using SqliteDataReader reader = command.ExecuteReader();
                foreach (CharTable expected in expectedCharacters)
                {
                    Require(reader.Read(), $"CharTable id={expected.Id} 행이 없습니다.");
                    CharTable actual = CharTable.Parser.ParseFrom((byte[])reader[2]);
                    Require(actual.Equals(expected) && reader.GetInt32(0) == expected.Id && reader.GetInt32(1) == expected.Type, $"CharTable id={expected.Id} 값이 원본과 다릅니다.");
                }
                Require(!reader.Read(), "CharTable에 기대하지 않은 추가 행이 있습니다.");
            }

            StatTable[] expectedStats =
            {
                new() { Id = 1, Hp = 10, Power = 5, Speed = 1 },
                new() { Id = 2, Hp = 20, Power = 10, Speed = 2 },
                new() { Id = 3, Hp = 30, Power = 15, Speed = 3 }
            };

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, payload FROM StatTable ORDER BY id";
                using SqliteDataReader reader = command.ExecuteReader();
                foreach (StatTable expected in expectedStats)
                {
                    Require(reader.Read(), $"StatTable id={expected.Id} 행이 없습니다.");
                    StatTable actual = StatTable.Parser.ParseFrom((byte[])reader[1]);
                    Require(actual.Equals(expected) && reader.GetInt32(0) == expected.Id, $"StatTable id={expected.Id} 값이 원본과 다릅니다.");
                }
                Require(!reader.Read(), "StatTable에 기대하지 않은 추가 행이 있습니다.");
            }
        }

        private static void ValidateParameters(SqliteConnection connection)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT payload FROM CharTable WHERE id = @id";
                SqliteParameter id = command.Parameters.Add("@id", SqliteType.Integer);
                id.Value = 2;
                CharTable character = CharTable.Parser.ParseFrom((byte[])command.ExecuteScalar());
                Require(character.Id == 2 && character.Name == "플레이어블2", "기본 키 파라미터 조회에 실패했습니다.");
                id.Value = -1;
                Require(command.ExecuteScalar() == null, "없는 기본 키 조회에 결과가 반환되었습니다.");
            }

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id FROM CharTable WHERE type = @type ORDER BY id";
                command.Parameters.Add("@type", SqliteType.Integer).Value = 1;
                using SqliteDataReader reader = command.ExecuteReader();
                Require(reader.Read() && reader.GetInt32(0) == 1, "type 조회의 첫 번째 행이 다릅니다.");
                Require(reader.Read() && reader.GetInt32(0) == 2, "type 조회의 두 번째 행이 다릅니다.");
                Require(!reader.Read(), "type 조회에 기대하지 않은 추가 행이 있습니다.");
            }

            using (SqliteCommand command = connection.CreateCommand())
            {
                const string TEXT_VALUE = "한글 ' OR 1=1 --";
                byte[] payload = new CharTable { Id = 7, Name = "바인딩 확인" }.ToByteArray();
                command.CommandText = "SELECT @text, @payload";
                command.Parameters.Add("@text", SqliteType.Text).Value = TEXT_VALUE;
                command.Parameters.Add("@payload", SqliteType.Blob).Value = payload;
                using SqliteDataReader reader = command.ExecuteReader();
                Require(reader.Read() && reader.GetString(0) == TEXT_VALUE && payload.SequenceEqual((byte[])reader[1]), "문자열 또는 BLOB 파라미터 값이 손상되었습니다.");
            }
        }

        private static void ValidateFailureCleanup(string databasePath)
        {
            try
            {
                using SqliteConnection connection = OpenReadOnly(databasePath);
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT FROM";
                using SqliteDataReader reader = command.ExecuteReader();
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 1)
            {
                return;
            }

            throw new InvalidOperationException("잘못된 SQL에서 예상한 오류가 발생하지 않았습니다.");
        }

        private static void VerifyReplacement(string sourcePath, string databasePath, string replacementPath)
        {
            // Windows에서 다른 연결이 파일을 열고 있으면 독점 열기나 교체가 실패해야 합니다.
            using (FileStream stream = new(databasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Require(stream.Length > 0, "검증 DB가 비어 있습니다.");
            }

            File.Copy(sourcePath, replacementPath);
            File.Replace(replacementPath, databasePath, null);
            using SqliteConnection connection = OpenReadOnly(databasePath);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM CharTable";
            Require(Convert.ToInt64(command.ExecuteScalar()) == 3, "교체 후 DB 조회에 실패했습니다.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
