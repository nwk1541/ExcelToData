# Voltage SQL Manager 구현 계획

## 문서 목적

이 문서는 ExcelToData가 생성한 SQLite 데이터베이스와 Protocol Buffers C# 클래스를 Unity 프로젝트에서 사용하기 위한 `SQL Manager` 패키지의 책임, 예정 API와 구현 순서를 정리합니다.

Excel 데이터 작성 및 변환 규약은 상위 [README](../README.md)를 따릅니다. 이 문서는 Unity 런타임 연동만 다루며 현재 `SqlManager` 본문은 구현하지 않습니다.

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

커스텀 쿼리도 같은 초기화 상태를 사용합니다.

```csharp
IReadOnlyList<CharacterSummary> result = SqlManager.Query("SELECT pk, payload FROM CharTable WHERE op = @op", reader => CharacterSummary.From(reader), SqlArgument.Create("@op", 2));
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
- 선택한 SQLite provider를 사용해 연결, command와 reader의 수명을 관리합니다.
- 테이블명과 parser가 결합된 `SqlTable<T>` 조회 facade를 제공합니다.
- 기존 런타임 DB의 `pk`, 선택적 `op`, `payload BLOB` 계약을 지원합니다.
- parameter binding 기반 커스텀 쿼리와 결과 mapper를 제공합니다.
- 프로젝트별 DB 파일, 생성 클래스 또는 배포 경로를 패키지에 포함하지 않습니다.

## 예정 API 계약

### 초기화와 종료

예정 진입점:

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
- 결과 없음과 단일 조회의 중복 결과를 구분해 처리합니다.

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

## 현재 구현 상태

| 항목 | 상태 | 내용 |
| --- | --- | --- |
| Excel 규약 검증 | 완료 | `#Range`, `#Type`, `pk`, `op`, 타입 및 데이터 값을 검증합니다. |
| Protobuf/C# 생성 | 완료 | `SchemaGenerator`가 `.proto`와 `Data.Local` C# 클래스를 생성합니다. |
| SQLite 생성 | 완료 | `DataExporter`가 런타임 DB와 디버그 DB를 생성하고 내용을 검증합니다. |
| Unity 프로젝트 | 구성됨 | Unity 프로젝트와 프로젝트 전용 `.gitignore`가 준비되어 있습니다. |
| UPM 패키지 구성 | 변경됨 | 표시 이름은 `SQL Manager`, assembly/root namespace는 `Noname.Voltage.Sql`입니다. |
| Protobuf 런타임 | 완료 | OpenUPM의 `org.nuget.google.protobuf` `3.35.1`을 패키지 종속성으로 사용합니다. |
| DB 경로 설정 에셋 | 제거됨 | 경로 준비 책임을 프로젝트로 옮겨 `LocalDataSettings`를 제거했습니다. |
| SQLite 런타임 | 미구현 | 관리 코드, provider와 플랫폼별 네이티브 라이브러리를 선택하지 않았습니다. |
| `SqlManager` | 미구현 | 초기화, 종료와 상태 검증 본문이 없습니다. |
| 테이블 조회 | 미구현 | `GetTable<T>()`, `GetByPk`, `GetByOp`가 없습니다. |
| 커스텀 쿼리 | 미구현 | `Query<T>`, `Execute`, `ExecuteScalar<T>`가 없습니다. |
| 자동 테스트 | 미구현 | 패키지 단위 테스트와 실제 DB 통합 테스트가 없습니다. |

## 현재 패키지 구성

```text
Packages/com.noname.voltage
  ├─ package.json
  ├─ README.md
  └─ Runtime
       └─ Noname.Voltage.Sql.asmdef
```

- 패키지 ID `com.noname.voltage`와 버전 `0.0.1`은 유지합니다.
- 표시 이름만 `LocalDataManager`에서 `SQL Manager`로 변경했습니다.
- `SqlManager`의 빈 클래스나 동작하지 않는 임시 API는 추가하지 않습니다.
- `Google.Protobuf`는 생성 코드와 향후 payload 파싱 흐름을 지원하기 위해 패키지 종속성으로 유지합니다.

## 구현 전에 결정할 사항

### 1. SQLite 구현

SQLite C# 래퍼만 추가해서는 Editor와 Player에서 완전하게 동작하지 않습니다. 다음을 하나의 의존성 세트로 선택해야 합니다.

- C# 쿼리 API
- SQLite provider 초기화
- 지원 플랫폼별 네이티브 SQLite 라이브러리
- Unity 플러그인 import 설정

초기 지원 범위는 Unity Editor on Windows와 Windows Standalone Player를 권장합니다. Android와 iOS는 기본 조회 흐름을 검증한 뒤 확장합니다.

### 2. 공개 쿼리 타입

`Query<T>()`의 mapper가 사용할 최소 reader 계약과 SQL parameter 표현을 확정해야 합니다. provider 고유 타입을 공개하면 구현 교체가 어려워지므로 `IDataRecord`와 패키지 소유 `SqlArgument` 같은 작은 계약을 우선 검토합니다.

### 3. 정적 진입점과 내부 컨텍스트

사용 측에서는 `SqlManager.Initialize`와 `SqlManager.GetTable()` 형태를 제공하되, 실제 상태와 동작은 내부 컨텍스트 인스턴스에 위임하는 구조를 검토합니다. 이를 통해 사용 편의성을 유지하면서 테스트 간 상태 초기화와 수명 검증을 가능하게 합니다.

## 단계별 구현 계획

### 1단계. SQLite 런타임 선택 및 연결 검증

- Windows Editor와 Windows Player에서 사용할 provider 및 네이티브 라이브러리를 선택합니다.
- 읽기 전용 연결을 열고 닫는 최소 검증을 수행합니다.
- 쿼리 종료 후 파일 잠금이 남지 않는지 확인합니다.

완료 조건: 실제 `LocalData.db` 연결을 열고 닫은 뒤 `DataExporter`가 같은 파일을 교체할 수 있습니다.

### 2단계. `SqlManager` 수명 구현

- `Initialize(string databasePath)`와 `Shutdown()`을 구현합니다.
- 초기화 전, 중복 초기화와 종료 후 호출에 대한 오류 계약을 구현합니다.
- 정적 진입점과 내부 컨텍스트의 책임을 분리합니다.

완료 조건: 정상 초기화와 각 잘못된 상태 전이가 테스트로 구분됩니다.

### 3단계. 테이블 조회 구현

- `GetTable<T>()`와 `SqlTable<T>`를 구현합니다.
- `pk` 단일 조회와 선택적 `op` 복수 조회를 parameter binding으로 구현합니다.
- `payload BLOB`을 호출자가 제공한 parser로 변환합니다.

완료 조건: `CharTable`과 `StatTable`의 실제 payload를 생성 원본과 같은 메시지로 역직렬화합니다.

### 4단계. 커스텀 쿼리 구현

- `Query<T>`, `Execute`와 `ExecuteScalar<T>`를 구현합니다.
- mapper와 parameter의 오류 처리 및 자원 해제를 검증합니다.
- 허용 범위와 transaction 지원 여부를 실제 사용 사례에 맞춰 결정합니다.

완료 조건: parameter를 사용하는 조회와 명령 실행이 성공하며 예외 이후에도 연결이 해제됩니다.

### 5단계. 생성 산출물 반영 자동화

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

### 6단계. 테스트와 Player 검증

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

## 전체 완료 기준

- [ ] Unity Editor와 첫 번째 지원 Player에서 컴파일 오류가 없습니다.
- [ ] 프로젝트가 준비한 DB 경로로 `SqlManager.Initialize`를 실행할 수 있습니다.
- [ ] `GetTable<T>()`로 pk와 op 조회를 수행할 수 있습니다.
- [ ] parameter binding 기반 커스텀 쿼리를 실행할 수 있습니다.
- [ ] 실제 payload를 프로젝트 생성 Protobuf 타입으로 변환할 수 있습니다.
- [ ] 조회가 끝난 뒤 SQLite 파일 잠금이 남지 않습니다.
- [ ] 프로젝트별 DB와 생성 C#은 패키지 외부에 유지됩니다.
- [ ] README의 절차만으로 새 환경에서 설치와 실행을 재현할 수 있습니다.

## 현재 보류 범위

다음 항목은 기본 조회 흐름과 실제 필요가 확인될 때까지 구현하지 않습니다.

- 전체 테이블 자동 preload와 장기 메모리 캐시
- 연결 풀과 장기 연결
- 비동기 SQL API
- 범용 ORM과 repository 추상화
- 자동 transaction 및 schema migration
- DB 다운로드, 패치와 핫 업데이트
- enum, FK/ref, `bytes`와 중첩 메시지 등 Excel 규약 확장
- Android와 iOS 등 추가 플랫폼 지원

## 권장 구현 순서

1. SQLite provider와 Windows 네이티브 라이브러리 선택
2. 실제 DB 연결 및 파일 잠금 검증
3. `SqlManager` 초기화와 종료 구현
4. `GetTable<T>()`, pk/op 조회와 payload parser 구현
5. 커스텀 쿼리 API 구현
6. 생성 산출물 반영 자동화
7. Editor 테스트와 Windows Player 통합 검증
