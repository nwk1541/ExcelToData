# SQL Manager

프로젝트가 준비한 SQLite 데이터베이스를 초기화한 뒤 테이블 조회와 커스텀 쿼리를 제공하기 위한 Unity 런타임 패키지입니다.

현재 Windows용 SQLite DLL을 포함하며, 프로젝트의 SampleScene에서 `Microsoft.Data.Sqlite`를 직접 사용하는 에디터 Play 검증을 완료했습니다. Windows Player 빌드·실행과 `SqlManager` 본문 구현은 아직 수행하지 않았습니다.

## SQLite 도입 방향

- `Microsoft.Data.Sqlite`와 필요한 `SQLitePCLRaw` 의존성 및 SQLite 네이티브 라이브러리를 함께 구성합니다.
- Windows 에디터와 Windows x64 Player를 우선 검증합니다.
- 실제 DB의 파라미터 조회, `payload BLOB`의 Protobuf 역직렬화와 연결 해제 후 파일 교체를 먼저 확인합니다.
- 최소 검증을 통과한 뒤 초기화·종료, 테이블 조회와 커스텀 쿼리를 구현합니다.
- Android·iOS의 네이티브 구성과 IL2CPP 실행 검증은 TODO로 남깁니다.

전체 작업 순서와 완료 조건은 [Voltage 구현 계획](../../README.md)에 정리합니다. 이 문서의 API 예시는 설치·실행 절차가 아닙니다.

## 포함된 SQLite 런타임

- `Microsoft.Data.Sqlite.Core` 10.0.10의 .NET Standard 2.0 DLL
- `SQLitePCLRaw.core`, `SQLitePCLRaw.provider.e_sqlite3` 2.1.11의 .NET Standard 2.0 DLL
- `SQLite` 3.53.4의 Windows x64 `e_sqlite3.dll`
- 적용 대상: Windows Editor 및 Windows x64 Player. Player 실제 동작은 미검증입니다.

`System.Memory`는 기존 Protobuf UPM 의존성에서 공급받으며 중복 DLL을 포함하지 않습니다. 출처, 라이선스 원문과 해시는 [Third Party Notices](<Third Party Notices.md>)를 참고합니다.

현재 provider 초기화는 프로젝트의 검증 코드에서 `SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3())`로 수행합니다. DB 준비 예제인 `DataManager`와 `SqliteValidation`은 패키지 외부에 있습니다. 실행 절차 및 확인 결과는 [SampleScene 실행](../../README.md#samplescene-실행)을 참고합니다.

## 책임 범위

프로젝트가 담당하는 항목:

- SQLite 데이터베이스의 생성, 다운로드, 복사와 패치
- `StreamingAssets`, `persistentDataPath` 등 배치 위치 결정
- 초기화에 전달할 최종 로컬 데이터베이스 경로 준비
- 프로젝트별 데이터베이스와 생성된 Protocol Buffers C# 클래스 소유

패키지가 담당할 항목:

- 준비된 데이터베이스 경로를 이용한 초기화
- SQLite 연결과 자원 수명 관리
- `GetTable<T>()`를 통한 테이블 조회 진입점
- parameter binding을 사용하는 커스텀 쿼리
- 호출자가 제공한 payload parser를 통한 결과 변환

패키지는 프로젝트별 데이터베이스, 생성된 C# 클래스 또는 플랫폼별 데이터 배포 정책을 포함하지 않습니다.

## 예정 사용 방식

다음 API는 목표 형태를 설명하기 위한 예시이며 아직 구현되지 않았습니다.

```csharp
string databasePath = await ProjectDatabaseLoader.PrepareAsync();

SqlManager.Initialize(databasePath);

SqlTable<CharTable> characters = SqlManager.GetTable<CharTable>("CharTable", bytes => CharTable.Parser.ParseFrom(bytes));

CharTable character = characters.GetByPk(1001);
IReadOnlyList<CharTable> types = characters.GetByOp(2);
```

커스텀 쿼리는 SQL 문자열, 결과 mapper와 parameter를 명시적으로 받는 형태를 목표로 합니다. 아래 SQL은 기본 키 컬럼이 `id`, 조회 컬럼이 `type`인 예시입니다. `pk`와 `op`는 필드의 역할이며 SQL에는 실제 컬럼명을 사용합니다.

```csharp
IReadOnlyList<CharacterSummary> result = SqlManager.Query("SELECT id, payload FROM CharTable WHERE type = @type", reader => CharacterSummary.From(reader), SqlArgument.Create("@type", 2));
```

## 설계 원칙

- `Initialize`는 데이터베이스를 검증하고 조회에 필요한 상태만 구성하며 전체 테이블을 미리 적재하지 않습니다.
- `GetTable<T>()`는 전체 데이터를 보관하는 컬렉션이 아니라 특정 테이블에 대한 조회 facade를 반환합니다.
- SQL 값은 문자열 결합 대신 parameter binding으로 전달합니다.
- 테이블명과 컬럼명은 SQL 식별자로 검증합니다.
- 연결, command와 reader는 작업이 끝나면 즉시 해제합니다.
- 초기 연결 구성은 `Pooling=False`로 검증하며, DB 경로와 배포 정책은 프로젝트가 결정합니다.
- 초기화 전 호출, 중복 초기화와 종료 후 호출의 동작을 명확한 오류 계약으로 정의합니다.
- 초기 범위에는 연결 풀, 자동 캐시, 범용 ORM과 비동기 조회를 포함하지 않습니다.

## 현재 구성

- 패키지 ID: `com.noname.voltage`
- 표시 이름: `SQL Manager`
- 런타임 assembly/root namespace: `Noname.Voltage.Sql`
- Protocol Buffers 런타임: `org.nuget.google.protobuf` `3.35.1`
- `SqlManager` 구현: 미구현
- SQLite 접근 API: `Microsoft.Data.Sqlite` 직접 사용
- SQLite 관리·네이티브 DLL: Windows용 포함, provider 초기화는 프로젝트 검증 코드에서 수행
- Windows 에디터 실행 검증: SampleScene Play 통과 (Unity 6000.5.2f1)
- Windows Player 빌드·실행 검증: 미수행
- Android·iOS 지원: TODO
