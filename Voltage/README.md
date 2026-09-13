# Voltage SQL Manager 구현 계획

## 문서 목적

이 문서는 ExcelToData가 생성한 SQLite 데이터베이스와 Protocol Buffers C# 클래스를 Unity 프로젝트에서 사용하기 위한 `SQL Manager` 패키지의 채택 기술, 책임, 예정 API와 검증 순서를 정리합니다.

Excel 데이터 작성 및 변환 규약은 상위 [README](../README.md)를 따릅니다. 패키지 개요는 [패키지 README](Packages/com.noname.voltage/README.md)를 참고합니다.

Windows용 SQLite DLL 구성과 SampleScene의 에디터 Play 검증을 완료했습니다. Windows x64 Player 빌드·실행 검증은 아직 수행하지 않았으며, 이후 `SqlManager` 본문을 구현합니다.

## SampleScene 실행

1. Windows의 Unity 6000.5.2f1에서 프로젝트를 열고 패키지 해석과 컴파일 완료를 기다립니다.
2. `Assets/Scenes/SampleScene.unity`를 열고 Play를 실행합니다.
3. `DataManager` 오브젝트의 `SqliteValidation`이 자동 실행됩니다. Console의 `[SQLite 검증 성공]`과 Inspector의 `Last Result`에서 결과를 확인합니다.
4. Play 중 같은 컴포넌트의 컨텍스트 메뉴 `Run SQLite Validation`으로 다시 실행할 수 있습니다. Play 밖에서는 실행하지 않습니다.

샘플은 다음 두 책임으로 나뉩니다.

- [DataManager.cs](Assets/Scripts/Data/DataManager.cs): `Initialize()`에서 `StreamingAssets/Database/LocalData.db`를 `persistentDataPath/VoltageSample/Database/LocalData.db`로 최초 한 번 복사하고 `DatabasePath`를 제공합니다. 중복 초기화는 예외이며, `Shutdown()` 및 `OnDestroy()`에서 경로 상태를 정리합니다. SQLite 연결·테스트 로직을 포함하지 않는 프로젝트 측 사용 예제입니다.
- [SqliteValidation.cs](Assets/Scripts/Validation/SqliteValidation.cs): 연결된 DataManager를 초기화한 다음 Windows provider 설정, 실제 DB 조회와 결과 검증을 수행합니다. `SqlManager`와 분리된 검증 전용 컴포넌트입니다.

검증은 준비된 로컬 DB를 실행별 임시 폴더에 복사한 뒤 `ReadOnly;Pooling=False`로 수행합니다. 정상·오류 이후의 파일 교체도 임시 복사본에만 적용하며, 실행이 끝나면 해당 임시 파일을 정리합니다. 배포 원본과 persistent DB는 교체하거나 삭제하지 않습니다.

샘플 DB를 갱신할 때는 `Assets/Database/LocalData.db`와 `Assets/StreamingAssets/Database/LocalData.db`, 생성 C# 및 검증 기대값을 함께 맞춰야 합니다. 기존 persistent DB는 자동 덮어쓰기하지 않으므로 프로젝트의 갱신 정책으로 별도 반영해야 합니다. 다운로드·패치 정책은 아직 구현하지 않았습니다.

### 확인한 결과 (2026-09-13)

- Unity 6000.5.2f1 Windows Editor에서 SQLite 3.53.4 로드 및 실제 Play 검증 통과
- `CharTable`, `StatTable` 각 3행의 SQL 키와 Protobuf 전체 필드가 Excel 샘플 값과 일치
- 기본 키 조회, 결과 없음, `type` 복수 조회, 한글 문자열과 BLOB 파라미터 바인딩 통과
- 정상 조회 및 SQL 오류 이후 연결 해제, 임시 DB 독점 열기·교체·재연결 통과
- 반복 검증, DataManager 중복 초기화 거부 및 종료 후 재초기화 통과
- 원본·배포 DB 보존 및 persistent DB 미덮어쓰기 확인

Windows Player의 DLL 포함·실행 여부와 Mono·IL2CPP 차이는 아직 미검증입니다. 이 결과를 Player 지원 완료로 해석하지 않습니다.

## 채택 방향과 지원 범위

- SQLite 접근 API는 `Microsoft.Data.Sqlite`를 직접 사용합니다.
- 출처가 명확한 라이브러리와 기존 `DataExporter`의 API 사용 경험을 활용하며, 별도의 제3자 Unity SQLite 래퍼는 도입하지 않습니다.
- `Microsoft.Data.Sqlite`에 필요한 `SQLitePCLRaw`와 플랫폼별 SQLite 네이티브 라이브러리는 함께 구성합니다.
- 1차 검증 대상은 Windows 에디터와 Windows x64 Standalone Player입니다.
- Android와 iOS는 TODO로 남기며, 현재 지원 또는 동작 검증 완료로 간주하지 않습니다.
- `SqlManager`의 빈 클래스나 동작하지 않는 임시 API는 추가하지 않습니다.

## 목표 사용 흐름

프로젝트가 플랫폼에 맞게 데이터베이스를 준비한 다음 `SqlManager`를 한 번 초기화하고 테이블 API 또는 커스텀 쿼리를 사용하도록 구성합니다.

```text
ExcelToData
  ├─ Output/CSharp/*.cs
  └─ Output/Database/LocalData.db
             ↓ 프로젝트별 반영 및 런타임 경로 준비
Unity Project
  ├─ Assets/CSharp/*.cs
  ├─ 프로젝트 소유 Database Loader
  └─ Packages/com.noname.voltage
       └─ SQL Manager
            → Initialize(databasePath)
            → GetTable<T>()
            → Query<T>() / Execute() / ExecuteScalar<T>()
            → payload parser를 이용한 결과 변환
```

다음 코드는 목표 API를 설명하기 위한 예시이며 아직 컴파일 가능한 구현이 아닙니다.

```csharp
string databasePath = await ProjectDatabaseLoader.PrepareAsync();

SqlManager.Initialize(databasePath);

SqlTable<CharTable> characters = SqlManager.GetTable<CharTable>("CharTable", bytes => CharTable.Parser.ParseFrom(bytes));

CharTable character = characters.GetByPk(1001);
IReadOnlyList<CharTable> types = characters.GetByOp(2);
```

커스텀 쿼리도 같은 초기화 상태를 사용합니다. 아래 SQL은 기본 키 컬럼이 `id`, 조회 컬럼이 `type`인 예시이며, 실제 SQL에는 각 테이블의 컬럼명을 사용합니다.

```csharp
IReadOnlyList<CharacterSummary> result = SqlManager.Query("SELECT id, payload FROM CharTable WHERE type = @type", reader => CharacterSummary.From(reader), SqlArgument.Create("@type", 2));
```

## 책임 경계

### 프로젝트 책임

- 각 프로젝트의 `LocalData.db`와 생성된 Protocol Buffers C# 클래스를 소유합니다.
- 데이터베이스 다운로드, 패치, 복사와 배치 위치를 결정합니다.
- `StreamingAssets`에서 읽거나 `persistentDataPath`로 복사하는 등 플랫폼별 준비 과정을 구현합니다.
- `SqlManager.Initialize`에 전달할 최종 로컬 파일 경로를 준비합니다.
- 사람이 확인하는 디버그 DB가 Player 런타임 산출물에 포함되지 않도록 관리합니다.

### 패키지 책임

- 프로젝트가 전달한 경로를 검증하고 SQL 조회 상태를 초기화합니다.
- `Microsoft.Data.Sqlite`를 사용해 연결, command와 reader의 수명을 관리합니다.
- 테이블명과 parser가 결합된 `SqlTable<T>` 조회 facade를 제공합니다.
- 기존 런타임 DB의 기본 키(`pk` 지정 필드), 선택적 조회 필드(`op` 지정 필드), `payload BLOB` 계약을 지원합니다.
- parameter binding 기반 커스텀 쿼리와 결과 mapper를 제공합니다.
- 프로젝트별 DB 파일, 생성 클래스 또는 배포 경로를 패키지에 포함하지 않습니다.

## 예정 API 계약

### 초기화와 종료

다음은 계약을 설명하는 표기이며 실행 가능한 C# 코드는 아닙니다.

```csharp
SqlManager.Initialize(string databasePath);
SqlManager.Shutdown();
```

- `databasePath`는 프로젝트가 준비한 읽을 수 있는 로컬 파일 경로입니다.
- 초기화 과정에서는 파일 존재 여부와 DB 연결 가능 여부를 확인합니다.
- 전체 테이블을 메모리에 미리 적재하지 않습니다.
- 초기화 전에 조회 API를 호출하면 명확한 예외를 발생시킵니다.
- 중복 초기화는 암묵적으로 기존 상태를 교체하지 않고 오류로 처리합니다.
- `Shutdown` 이후에는 보유한 상태와 자원을 정리합니다.

### 테이블 조회

예정 진입점:

```csharp
SqlTable<T> SqlManager.GetTable<T>(string tableName, Func<byte[], T> payloadParser);
```

- `GetTable<T>()`는 전체 테이블 데이터가 아니라 재사용 가능한 조회 facade를 반환합니다.
- table facade는 `GetByPk`와 `GetByOp`를 우선 제공합니다.
- `payloadParser`는 패키지가 프로젝트별 생성 타입을 직접 알지 않도록 분리합니다.
- 테이블명은 허용된 SQL 식별자 형식으로 검증합니다.
- 결과가 없는 경우의 반환 또는 예외 규칙은 구현 전에 확정합니다.

### 커스텀 쿼리

초기 범위의 예정 API:

```csharp
IReadOnlyList<T> SqlManager.Query<T>(string sql, Func<IDataRecord, T> mapper, params SqlArgument[] arguments);
int SqlManager.Execute(string sql, params SqlArgument[] arguments);
T SqlManager.ExecuteScalar<T>(string sql, params SqlArgument[] arguments);
```

- SQL 값은 문자열 보간이나 결합 대신 `SqlArgument`로 전달합니다.
- 결과 타입 변환은 호출자가 제공한 mapper가 담당합니다.
- 패키지는 provider 고유 reader 타입을 공개 API로 노출하지 않습니다.
- 여러 문장 실행, transaction과 schema 변경 API는 실제 필요가 확인된 뒤 별도로 검토합니다.

### 연결 수명

- 초기 구현은 쿼리마다 연결을 짧게 열고 닫는 방식을 우선합니다.
- command와 reader는 성공 및 예외 경로 모두에서 즉시 해제합니다.
- Unity Editor에서 조회한 뒤에도 `DataExporter`가 DB 파일을 교체할 수 있어야 합니다.
- 연결 풀이나 장기 연결은 실제 성능 측정 전에는 도입하지 않습니다.
- `Microsoft.Data.Sqlite`의 연결 문자열에는 `Pooling=False`를 명시하는 방향으로 검증합니다.

## 현재 구현 상태

| 항목 | 상태 | 내용 |
| --- | --- | --- |
| Unity 검증용 데이터 | 배치됨 | `Assets/Database/LocalData.db`, `Assets/CSharp` 생성 클래스 및 `Assets/StreamingAssets/Database/LocalData.db` 배포 복사본이 있습니다. |
| 프로젝트 DataManager | 예제 구현됨 | 최초 DB 복사, 최종 경로 제공 및 초기화·종료 상태를 담당합니다. |
| UPM 패키지 구성 | 구성됨 | 패키지 ID는 `com.noname.voltage`, 버전은 `0.0.1`, 표시 이름은 `SQL Manager`입니다. |
| 런타임 어셈블리 | 구성됨 | assembly/root namespace는 `Noname.Voltage.Sql`입니다. |
| Protobuf 런타임 | 에디터 연동 확인 | OpenUPM의 `org.nuget.google.protobuf` `3.35.1`로 실제 payload를 역직렬화했습니다. |
| SQLite 접근 API | 도입됨 | `Microsoft.Data.Sqlite.Core` 10.0.10의 .NET Standard 2.0 DLL을 직접 사용합니다. |
| SQLite DLL 구성 | Windows용 구성됨 | SQLitePCLRaw 2.1.11, 네이티브 SQLite 3.53.4 및 Windows 전용 플러그인 설정을 포함합니다. |
| 에디터 검증 | 통과 | SampleScene Play에서 조회·역직렬화·파라미터 바인딩·파일 잠금 해제를 확인했습니다. |
| Windows Player 검증 | 미수행 | 실제 x64 빌드, 네이티브 DLL 포함 여부 및 실행 검증이 남았습니다. |
| `SqlManager` | 미구현 | 초기화, 종료와 상태 검증 본문이 없습니다. |
| 테이블 조회 | 미구현 | `GetTable<T>()`, `GetByPk`, `GetByOp`가 없습니다. |
| 커스텀 쿼리 | 미구현 | `Query<T>`, `Execute`, `ExecuteScalar<T>`가 없습니다. |
| 검증 코드 | 프로젝트에 구현됨 | `SqliteValidation`이 Play 시작 시 실행됩니다. Unity Test Runner용 테스트는 아직 없습니다. |
| Android·iOS | TODO | 플랫폼별 네이티브 구성과 IL2CPP 실행 검증을 보류합니다. |

## 현재 패키지 구성

```text
Packages/com.noname.voltage
  ├─ package.json
  ├─ README.md
  ├─ Third Party Notices.md
  ├─ Third Party Licenses/
  └─ Runtime
       ├─ Noname.Voltage.Sql.asmdef
       └─ Plugins
            ├─ Managed/ (Microsoft.Data.Sqlite 및 SQLitePCLRaw DLL)
            └─ Windows/x86_64/e_sqlite3.dll
```

위 구조는 현재 파일 구성입니다. 프로젝트 DB, 생성 C#과 DataManager·검증 스크립트는 패키지 밖 `Assets`에 유지합니다.

## SQLite 의존성 구성

`Microsoft.Data.Sqlite.dll` 하나만 복사하지 않고 다음 구성을 함께 포함했습니다.

| 구성 | 확인할 내용 |
| --- | --- |
| 관리 API | `Microsoft.Data.Sqlite`의 Unity 호환 어셈블리와 의존성 |
| 네이티브 호출 계층 | `SQLitePCLRaw.core`, provider와 필요한 초기화 구성 |
| SQLite 엔진 | Windows x64용 네이티브 DLL과 provider가 참조하는 라이브러리 이름 |
| Unity 설정 | 관리 DLL 참조, 네이티브 플러그인의 OS·CPU 및 에디터·Player 적용 대상 |

현재 [DataExporter](../Project/DataExporter/DataExporter.csproj)는 `Microsoft.Data.Sqlite` `10.0.10`과 `SQLitePCLRaw.bundle_e_sqlite3` `3.0.5`를 사용합니다. Voltage는 `Microsoft.Data.Sqlite.Core` `10.0.10`, `SQLitePCLRaw.core` 및 `provider.e_sqlite3` `2.1.11`, Windows x64 네이티브 `SQLite` `3.53.4`를 사용합니다. 관리 DLL은 .NET Standard 2.0 대상으로 선택했으며 Exporter의 `net10.0` 출력물을 사용하지 않습니다.

기존 Protobuf UPM 의존성의 `System.Memory` 4.5.3을 유지하기 위해 SQLitePCLRaw는 요구 최소 버전인 2.1.11을 선택했습니다. `Batteries_V2` 대신 첫 연결 전에 다음 provider를 명시적으로 설정합니다.

```csharp
SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3());
```

DLL의 버전, 출처, 라이선스, SHA-256 및 적용 대상은 [Third Party Notices](<Packages/com.noname.voltage/Third Party Notices.md>)에 기록했습니다. 참고: [Microsoft.Data.Sqlite 네이티브 구성 문서](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/custom-versions).

## API 구현 전에 확정할 사항

- **쿼리 타입:** `IDataRecord`와 패키지 소유 `SqlArgument`를 기준으로 mapper와 파라미터 계약을 확정합니다.
- **테이블 조회:** 실제 기본 키·조회 컬럼명과 타입을 연결하는 방식, `GetByPk`의 결과 없음 처리, `op`가 없는 테이블의 동작을 확정합니다. `pk`와 `op`는 Excel 필드의 역할을 나타내며 실제 컬럼명이 항상 해당 문자열인 것은 아닙니다.
- **읽기·쓰기 정책:** 최소 검증은 읽기 전용으로 수행합니다. 이후 `Execute`를 구현할 때 연결 모드와 허용 범위를 결정합니다.
- **상태 관리:** 정적 `SqlManager` 진입점의 초기화·종료와 테스트 간 상태 정리 방식을 확정합니다. 내부 컨텍스트 분리 여부는 구현에 필요한 범위에서 결정합니다.
- **Windows 빌드:** 검증할 스크립팅 백엔드(Mono/IL2CPP)를 정하고 결과에 명시합니다. 한 백엔드의 성공을 다른 백엔드의 검증 완료로 간주하지 않습니다.

## 단계별 구현 계획

### 1단계. Windows용 DLL 구성

상태: 완료. SampleScene의 Windows Editor Play에서 네이티브 DLL 로드까지 확인했습니다.

- `Microsoft.Data.Sqlite` 관리 DLL과 필요한 `SQLitePCLRaw` 의존성 및 Windows x64 네이티브 DLL을 구성합니다.
- Unity 플러그인 적용 대상을 Windows 에디터와 Windows Player에 맞춥니다.
- 라이브러리 출처, 버전, 라이선스와 provider 초기화 방법을 기록합니다.

완료 조건: 관리 DLL의 참조 오류가 없고 Windows 에디터에서 네이티브 SQLite를 로드할 수 있습니다.

### 2단계. 에디터와 Windows Player 최소 검증

상태: 프로젝트 측 DataManager·검증 컴포넌트와 에디터 검증 완료. Windows Player 빌드·실행은 다음 작업입니다.

- `SqlManager`를 구현하기 전에 프로젝트 측 최소 검증 코드로 동작을 확인합니다.
- 기존 런타임 `LocalData.db`의 검증용 복사본을 준비하고 읽기 전용, `Pooling=False`로 연결합니다.
- 실제 기본 키 또는 조회 컬럼에 파라미터를 바인딩해 행을 조회합니다.
- `payload`를 `byte[]`로 읽고 `CharTable.Parser.ParseFrom()` 등 생성된 parser로 역직렬화합니다.
- 결과를 생성 원본과 비교하고, 연결 종료 후 복사본 교체가 가능한지 확인합니다.
- Windows Player에서도 프로젝트가 DB 경로를 준비하도록 하고 같은 검증을 수행합니다.
- Player 산출물의 네이티브 DLL 포함 여부와 실제 로드를 확인하고 사용한 스크립팅 백엔드를 기록합니다.

완료 조건: 에디터와 Windows x64 Player에서 동일한 조회·역직렬화가 성공하고, 연결 종료 후 DB 파일 잠금이 남지 않습니다. 원본 DB는 검증 과정에서 변경하지 않습니다.

### 3단계. `SqlManager` 수명 구현

- `Initialize(string databasePath)`와 `Shutdown()`을 구현합니다.
- 초기화 전, 중복 초기화와 종료 후 호출에 대한 오류 계약을 구현합니다.
- 확정한 상태 관리 방식으로 자원 정리와 테스트 간 상태 초기화를 구현합니다.

완료 조건: 정상 초기화와 각 잘못된 상태 전이가 테스트로 구분됩니다.

### 4단계. 테이블 조회 구현

- `GetTable<T>()`와 `SqlTable<T>`를 구현합니다.
- `pk` 단일 조회와 선택적 `op` 복수 조회를 parameter binding으로 구현합니다.
- `payload BLOB`을 호출자가 제공한 parser로 변환합니다.

완료 조건: `CharTable`과 `StatTable`의 실제 payload를 생성 원본과 같은 메시지로 역직렬화합니다.

### 5단계. 커스텀 쿼리 구현

- `Query<T>`, `Execute`와 `ExecuteScalar<T>`를 구현합니다.
- mapper와 parameter의 오류 처리 및 자원 해제를 검증합니다.
- 허용 범위와 transaction 지원 여부를 실제 사용 사례에 맞춰 결정합니다.

완료 조건: parameter를 사용하는 조회와 명령 실행이 성공하며 예외 이후에도 연결이 해제됩니다.

### 6단계. 생성 산출물 반영 자동화

프로젝트 측 작업으로 다음 복사를 자동화합니다.

```text
Output/CSharp/*.cs
  → Voltage/Assets/CSharp/

Output/Database/LocalData.db
  → 프로젝트가 선택한 Unity 데이터 경로
```

- 생성 C# 대상 폴더는 전체 교체해 삭제된 테이블 파일을 남기지 않습니다.
- 디버그 DB는 Player 런타임 산출물에 포함하지 않습니다.
- 이 과정은 프로젝트 데이터에 속하므로 패키지 내부 경로로 고정하지 않습니다.

### 7단계. 패키지 통합 검증

최소 검증에 사용한 데이터로 구현된 공개 API를 검증합니다. 쓰기 쿼리와 파일 교체 검증은 검증용 DB 복사본을 대상으로 합니다.

필수 검증:

- 초기화 성공, 초기화 전 호출, 중복 초기화와 종료 후 호출
- DB 파일 누락과 잘못된 SQLite 파일
- `GetByPk` 성공 및 결과 없음
- `GetByOp` 복수 조회
- 커스텀 parameter 쿼리와 결과 mapper
- 실제 `payload BLOB`의 Protobuf 역직렬화
- 예외 이후 connection, command와 reader 해제
- 조회 이후 DB 파일 교체 가능 여부
- Windows Standalone Player에서 동일 흐름 실행

## 1차 지원 범위의 완료 기준

- [ ] Windows 에디터와 Windows x64 Player에서 관리·네이티브 DLL이 정상 로드됩니다.
- [ ] 검증에 사용한 DLL 버전, 초기화 방식과 Windows 스크립팅 백엔드가 기록되어 있습니다.
- [ ] 프로젝트가 준비한 DB 경로로 `SqlManager.Initialize`를 실행할 수 있습니다.
- [ ] `GetTable<T>()`로 pk와 op 조회를 수행할 수 있습니다.
- [ ] parameter binding 기반 커스텀 쿼리를 실행할 수 있습니다.
- [ ] 실제 payload를 프로젝트 생성 Protobuf 타입으로 변환할 수 있습니다.
- [ ] 조회가 끝난 뒤 SQLite 파일 잠금이 남지 않습니다.
- [ ] 프로젝트별 DB와 생성 C#은 패키지 외부에 유지됩니다.
- [ ] README의 절차만으로 새 환경에서 설치와 실행을 재현할 수 있습니다.

## TODO: Android·iOS

Windows 최소 검증 이후 별도로 진행합니다. 아래 항목은 1차 완료 조건에 포함하지 않습니다.

- [ ] Android 대상 CPU별 네이티브 라이브러리, 16KB 페이지 크기 대응과 플러그인 설정 확인
- [ ] iOS 네이티브 라이브러리의 링크 방식과 provider 초기화 확인
- [ ] 각 플랫폼의 IL2CPP 빌드 및 실기기에서 조회·역직렬화·자원 해제 검증
- [ ] 프로젝트 측 DB 배포·로컬 경로 준비 방식 검증

## 보류 범위

다음 항목은 기본 조회 흐름과 실제 필요가 확인될 때까지 구현하지 않습니다.

- 전체 테이블 자동 preload와 장기 메모리 캐시
- 연결 풀과 장기 연결
- 비동기 SQL API
- 범용 ORM과 repository 추상화
- 자동 transaction 및 schema migration
- DB 다운로드, 패치와 핫 업데이트
- enum, FK/ref, `bytes`와 중첩 메시지 등 Excel 규약 확장
