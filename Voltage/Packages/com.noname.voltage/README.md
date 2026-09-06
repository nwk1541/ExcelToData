# SQL Manager

프로젝트가 준비한 SQLite 데이터베이스를 초기화한 뒤 테이블 조회와 커스텀 쿼리를 제공하기 위한 Unity 런타임 패키지입니다.

현재는 패키지 구성과 공개 계약만 정리된 단계입니다. `SqlManager`, SQLite provider, 테이블 조회 및 커스텀 쿼리 본문은 아직 구현하지 않았습니다.

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

커스텀 쿼리는 SQL 문자열, 결과 mapper와 parameter를 명시적으로 받는 형태를 목표로 합니다.

```csharp
IReadOnlyList<CharacterSummary> result = SqlManager.Query("SELECT pk, payload FROM CharTable WHERE op = @op", reader => CharacterSummary.From(reader), SqlArgument.Create("@op", 2));
```

## 설계 원칙

- `Initialize`는 데이터베이스를 검증하고 조회에 필요한 상태만 구성하며 전체 테이블을 미리 적재하지 않습니다.
- `GetTable<T>()`는 전체 데이터를 보관하는 컬렉션이 아니라 특정 테이블에 대한 조회 facade를 반환합니다.
- SQL 값은 문자열 결합 대신 parameter binding으로 전달합니다.
- 테이블명과 컬럼명은 SQL 식별자로 검증합니다.
- 연결, command와 reader는 작업이 끝나면 즉시 해제합니다.
- 초기화 전 호출, 중복 초기화와 종료 후 호출의 동작을 명확한 오류 계약으로 정의합니다.
- 초기 범위에는 연결 풀, 자동 캐시, 범용 ORM과 비동기 조회를 포함하지 않습니다.

## 현재 구성

- 패키지 ID: `com.noname.voltage`
- 표시 이름: `SQL Manager`
- 런타임 assembly/root namespace: `Noname.Voltage.Sql`
- Protocol Buffers 런타임: `org.nuget.google.protobuf` `3.35.1`
- `SqlManager` 구현: 미구현
- SQLite provider 및 네이티브 라이브러리: 미선정
