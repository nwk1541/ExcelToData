# Local Data

프로젝트가 제공하는 SQLite 데이터베이스와 Protocol Buffers 생성 클래스를 읽기 위한 Unity 런타임 패키지입니다.

## 프로젝트 설정

1. `Assets`에 SQLite 데이터베이스와 Protocol Buffers C# 생성 클래스를 둡니다.
2. `Assets/Create/Local Data/Settings` 메뉴에서 `LocalDataSettings` 에셋을 생성합니다.
3. `Relative Database Path`에 `Assets` 기준 데이터베이스 상대 경로를 지정합니다.

현재 테스트 프로젝트의 기본 경로는 `Database/LocalData.db`입니다.
