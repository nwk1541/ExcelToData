using System;
using System.IO;
using UnityEngine;

namespace Voltage.Sample
{
    /// <summary>
    /// 프로젝트가 배포 DB와 최종 로컬 경로를 소유하는 사용 예제입니다.
    /// SQLite 연결과 조회는 이 컴포넌트에서 담당하지 않습니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DataManager : MonoBehaviour
    {
        private const string DATABASE_DIRECTORY = "VoltageSample/Database";
        private const string DATABASE_FILE_NAME = "LocalData.db";

        public string DatabasePath { get; private set; }
        public bool IsInitialized => !string.IsNullOrEmpty(DatabasePath);

        public void Initialize()
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException("DataManager가 이미 초기화되었습니다.");
            }

            if (Application.platform != RuntimePlatform.WindowsEditor && Application.platform != RuntimePlatform.WindowsPlayer)
            {
                throw new PlatformNotSupportedException("현재 DB 준비 예제는 Windows 에디터와 Player만 지원합니다. Android와 iOS는 TODO입니다.");
            }

            string sourcePath = Path.Combine(Application.streamingAssetsPath, "Database", DATABASE_FILE_NAME);
            string databaseDirectory = Path.Combine(Application.persistentDataPath, DATABASE_DIRECTORY);
            string databasePath = Path.Combine(databaseDirectory, DATABASE_FILE_NAME);

            Directory.CreateDirectory(databaseDirectory);

            // 최초 실행에만 배포 DB를 복사합니다. 기존 로컬 DB의 갱신·패치는 프로젝트 정책으로 구현합니다.
            if (!File.Exists(databasePath))
            {
                File.Copy(sourcePath, databasePath);
            }

            DatabasePath = databasePath;
            // SqlManager 구현 이후 이 경로를 SqlManager.Initialize에 전달합니다.
        }

        public void Shutdown()
        {
            // 현재는 경로만 보유합니다. SqlManager 구현 이후에는 해당 자원도 여기서 종료합니다.
            DatabasePath = null;
        }

        private void OnDestroy()
        {
            Shutdown();
        }
    }
}
