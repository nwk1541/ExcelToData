# ExcelToData

Excel로 작성한 게임 기획 데이터를 검증한 뒤, 런타임 SQLite 데이터베이스와 사람이 직접 확인하는 디버그 SQLite 데이터베이스로 변환하는 테스트 프로젝트입니다.

## 변환 흐름

~~~text
Excel Workbook
  → 규약 검증
  → Protocol Buffers 직렬화
  → 런타임 SQLite 생성
  → 디버그 SQLite 생성
~~~

## 산출물

~~~text
Proto/
  CharTable.proto
  StatTable.proto

Output/
  CSharp/                  # Unity 등 외부 C# 프로젝트가 역직렬화에 사용하는 생성 클래스
    CharTable.cs
    StatTable.cs
  Database/
    LocalData.db           # 게임 런타임이 pk, op, payload로 조회하는 DB
    Debug/
      LocalData.db         # 사람이 모든 기획 컬럼을 직접 확인하는 DB
~~~

- 워크시트 하나는 .proto 파일 하나와 C# 클래스 하나에 대응합니다.
- 모든 생성 C# 클래스는 Data.Local namespace에 포함됩니다.
- Proto는 스키마 계약 파일이며, Output/CSharp와 Output/Database는 외부 도구에 전달하는 생성 산출물입니다.
- `Output/Database/LocalData.db`는 런타임에서 사용하는 데이터베이스입니다.
- `Output/Database/Debug/LocalData.db`는 사람이 직접 데이터를 확인하는 디버그 데이터베이스이며, 런타임에서는 사용하지 않습니다.
- DataExporter는 변환 후 런타임 DB의 테이블 행 수, pk 조회, op 값·인덱스, `payload` 역직렬화와 디버그 DB의 모든 컬럼 값을 검증합니다.

## 핵심 규약

| 항목 | 규칙 |
| --- | --- |
| 워크시트 | 워크시트 하나는 SQLite 테이블 하나에 대응합니다. 시트 이름은 영문자(A-Z, a-z)만 허용하며 SQLite 테이블 이름으로 그대로 사용합니다. |
| #Range | 워크시트마다 정확히 하나를 선언합니다. 대상 범위 밖의 셀에 두며, 헤더·타입·데이터 행을 포함하는 연속된 직사각형 범위를 지정합니다. |
| #Type | 워크시트마다 정확히 하나를 선언합니다. 타입 행의 첫 셀을 지정하며, 해당 행은 헤더 바로 다음 행이고 #Range와 같은 열 폭을 가져야 합니다. |
| 헤더 | 필드명 또는 필드명:속성 형식입니다. 필드명은 비어 있거나 중복될 수 없으며, SQL 컬럼 이름으로 그대로 사용합니다. SQL 생성 시에는 식별자를 인용 처리합니다. |
| 데이터 | 타입 행 바로 다음 행부터 시작합니다. 런타임 DB에서는 각 데이터 행 전체를 Protocol Buffers로 직렬화해 `payload` BLOB에 저장하고, 디버그 DB에서는 모든 필드를 독립 SQLite 컬럼으로 저장합니다. |
| 수식 | 수식을 실행하지 않고 Excel 파일에 저장된 계산값만 읽습니다. 계산값이 없으면 변환 오류입니다. |

~~~text
A2: #Range=C2:G6
A3: #Type=C3

C2: id:pk    D2: type:op    E2: statId    F2: skillIds         G2: name
C3: int32    D3: int32      E3: int32      F3: repeated<int32>  G3: string
C4: 1         D4: 1          E4: 1          F4: 1,2               G4: 플레이어블1
C5: 2         D5: 1          E5: 2          F5: 1,2               G5: 플레이어블2
C6: 3         D6: 2          E6: 3          F6: 1,2,3             G6: 몬스터1
~~~

- #Range 형식: #Range={시작 셀}:{끝 셀}. 예: #Range=C2:G6
- #Type 형식: #Type={타입 행의 첫 셀}. 예: #Type=C3
- 선언은 셀 전체 값이 정확히 일치할 때만 인식합니다.

## 컬럼 속성

| 표기 | 규칙 |
| --- | --- |
| 필드명 | 런타임 DB에서는 `payload`에만 저장하고, 디버그 DB에서는 독립 SQLite 컬럼으로 저장합니다. |
| 필드명:pk | 모든 워크시트에 정확히 하나가 필수입니다. int32, int64, string 같은 단일 값 타입만 허용하며, 값은 비어 있거나 중복될 수 없습니다. 런타임 DB의 SQLite PK 컬럼과 `payload`, 디버그 DB의 SQLite PK 컬럼에 저장됩니다. |
| 필드명:op | 런타임 DB에서 독립 NOT NULL 컬럼과 단일 컬럼 인덱스를 생성하며, 값은 `payload`에도 함께 저장됩니다. 디버그 DB에서는 독립 컬럼으로만 저장하고 별도 인덱스는 생성하지 않습니다. repeated\<T\>와 중첩 메시지 타입에는 지정할 수 없습니다. |

## 자료형

자료형은 Protocol Buffers 호환 논리 타입을 기준으로 작성합니다. 런타임 DB는 pk 또는 op 필드에만 SQLite 타입 매핑을 적용하고, 디버그 DB는 모든 필드에 타입 매핑을 적용합니다.

| Excel 타입 | 런타임 SQLite 타입 | 디버그 SQLite 타입 |
| --- | --- | --- |
| int32, int64 | INTEGER | INTEGER |
| float, double | REAL | REAL |
| bool | INTEGER (0 또는 1) | INTEGER (0 또는 1) |
| string | TEXT | TEXT |
| repeated\<T\> | 지원하지 않음 (`payload` 전용) | TEXT (JSON 배열) |

- bool 값은 0 또는 1만 허용합니다.
- 목록 셀은 쉼표(,)로 값을 구분합니다.
- repeated\<string\>의 값에는 쉼표를 포함할 수 없습니다.
- 디버그 DB의 repeated\<T\>는 JSON 배열 TEXT로 저장합니다. 예: `1, 2, 3` → `[1,2,3]`
- 디버그 DB에는 `payload` BLOB을 저장하지 않습니다.
- string 타입의 빈 셀은 빈 문자열로, repeated\<T\> 타입의 빈 셀은 빈 목록으로 처리합니다.
- string과 repeated\<T\>를 제외한 모든 타입의 빈 셀은 변환 오류입니다.

## 변환 실패 조건

- #Range 또는 #Type 선언이 없거나 둘 이상인 경우
- 시트 이름에 영문자 이외의 문자가 포함된 경우
- 선언 형식, 참조 범위, 헤더·타입 행 위치가 규약과 다른 경우
- 지원하지 않는 타입을 선언했거나, 타입 행과 헤더의 열 폭이 다른 경우
- bool 값이 0 또는 1이 아니거나, 데이터 셀을 선언한 타입으로 변환할 수 없는 경우
- 수식 셀에 저장된 계산값이 없는 경우
- string과 repeated\<T\>를 제외한 타입에 빈 셀이 있는 경우
- pk 필드가 없거나 둘 이상인 경우, 또는 PK 값이 누락·중복된 경우

## Protocol Buffers 필드 번호

필드 번호는 #Range의 헤더 열을 왼쪽에서 오른쪽으로 읽어 1부터 순서대로 자동 생성합니다. pk와 op 속성은 번호 순서에 영향을 주지 않습니다.

열을 삽입·삭제·이동하면 이후 필드 번호가 바뀌므로 기존 런타임 `payload` BLOB과 호환되지 않습니다. Excel 구조를 변경한 뒤에는 런타임 DB와 디버그 DB를 모두 다시 생성합니다.
