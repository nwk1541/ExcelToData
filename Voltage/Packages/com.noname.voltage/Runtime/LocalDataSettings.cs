using System;
using System.IO;
using UnityEngine;

namespace Noname.Voltage.Data
{
    [CreateAssetMenu(fileName = "LocalDataSettings", menuName = "Local Data/Settings")]
    public sealed class LocalDataSettings : ScriptableObject
    {
        [Tooltip("Assets 폴더를 기준으로 한 SQLite 데이터베이스의 상대 경로입니다.")]
        [SerializeField] private string _relativeDatabasePath = "Database/LocalData.db";

        public string RelativeDatabasePath => _relativeDatabasePath;

        public string GetDatabasePath()
        {
            if (string.IsNullOrWhiteSpace(_relativeDatabasePath))
            {
                throw new InvalidOperationException("SQLite 데이터베이스 상대 경로가 비어 있습니다.");
            }

            if (Path.IsPathRooted(_relativeDatabasePath))
            {
                throw new InvalidOperationException("SQLite 데이터베이스 경로는 Assets 기준 상대 경로여야 합니다.");
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, _relativeDatabasePath));
        }
    }
}
