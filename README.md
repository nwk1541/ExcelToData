# ExcelToData

Excel로 작성한 게임 기획 데이터를 검증한 뒤, Protocol Buffers Payload를 포함한 SQLite 데이터베이스로 변환하는 실험 프로젝트입니다.

## 목표

~~~text
Excel Workbook
  → 규약 검증
  → Protocol Buffers 직렬화
  → SQLite 생성
~~~

- 워크시트 하나는 SQLite 테이블 하나에 대응하며 시트 이름을 테이블 이름으로 사용합니다.
- 모든 데이터 워크시트에는 정확히 하나의 pk 필드가 있어야 합니다.
- 각 데이터 행 전체는 Protocol Buffers로 직렬화되어 SQLite의 Payload BLOB에 저장됩니다.

## 워크시트 규약

각 데이터 워크시트에는 정확히 하나의 `#Range` 선언과 `#Type` 선언이 있어야 합니다. 선언 셀은 대상 범위 밖에 두며, 범위와 같은 행에 있어도 대상 열 밖이면 됩니다.

~~~text
A2: #Range=C2:G6
A3: #Type=C3

C2: id:pk    D2: type:op            E2: stat_id    F2: skill_ids         G2: name
C3: int32    D3: enum:CharType      E3: int32      F3: repeated<int32>  G3: string
C4: 1         D4: Char               E4: 1          F4: 1,2               G4: 플레이어블1
C5: 2         D5: Char               E5: 2          F5: 1,2               G5: 플레이어블2
C6: 3         D6: Monster            E6: 3          F6: 1,2,3             G6: 몬스터1
~~~

### #Range

- 형식: #Range={시작 셀}:{끝 셀}, 예: #Range=C2:G6
- 동일 워크시트 안의 연속된 직사각형 범위만 허용합니다.
- 범위에는 헤더 행, 타입 행, 데이터 행을 모두 포함합니다.
- 선언은 셀 전체 값이 정확히 일치할 때만 인식합니다.

### #Type

- 형식: #Type={타입 행의 첫 셀}, 예: #Type=C3
- 타입 행은 #Range의 헤더 바로 다음 행이어야 합니다.
- 타입 행의 시작 열과 끝 열은 #Range의 시작 열과 끝 열을 따릅니다.
- 실제 데이터는 타입 행 바로 다음 행부터 시작합니다.

## 헤더와 컬럼 속성

헤더는 필드명 또는 필드명:속성 형식입니다.

~~~text
id:pk
type:op
name
~~~

- 헤더 필드명은 비어 있거나 중복될 수 없습니다.
- 속성이 없는 필드는 Protocol Buffers Payload에만 저장합니다.

### pk

- 워크시트마다 정확히 하나의 필드에만 지정합니다.
- int32, int64, string 같은 단일 값 타입에만 지정합니다.
- 모든 데이터 행에서 값이 비어 있지 않고 유일해야 합니다.
- 해당 필드는 SQLite의 PK 컬럼과 Payload에 모두 저장됩니다.

### op

- 해당 값을 SQLite의 독립 컬럼으로도 저장하고, 인덱스를 생성합니다.
- repeated\<T\>와 같은 리스트 또는 중첩 메시지 타입에는 지정하지 않습니다.

예를 들어 type:op는 아래와 같은 SQLite 구조를 만듭니다.

~~~sql
CREATE TABLE CharTable (
    id INTEGER PRIMARY KEY,
    type INTEGER NOT NULL,
    Payload BLOB NOT NULL
);

CREATE INDEX IX_CharTable_type ON CharTable(type);
~~~

## 자료형

자료형은 SQLite 타입이 아니라 Protocol Buffers 호환 논리 타입을 기준으로 작성합니다. SQLite 컬럼이 필요한 경우에만 아래 매핑을 사용합니다.

| Excel 타입 | 용도 | pk/op SQLite 타입 |
| --- | --- | --- |
| int32, int64 | 정수 | INTEGER |
| float, double | 실수 | REAL |
| bool | 불리언 | INTEGER (0 또는 1) |
| string | 문자열 | TEXT |
| bytes | 바이트 배열 | BLOB |
| enum:EnumName | Protocol Buffers enum | INTEGER |
| repeated\<T\> | 같은 타입의 목록 | 지원하지 않음 (Payload 전용) |

- enum:CharType처럼 enum 이름을 반드시 명시합니다.
- enum 값의 숫자는 기존 데이터와의 호환성을 위해 변경하지 않습니다.
- 목록 셀은 쉼표(,)로 값을 구분하며, 각 값의 앞뒤 공백은 제거합니다.
- 빈 목록 셀은 빈 목록으로 처리합니다.

## 검증 규칙

- #Range와 #Type 선언은 워크시트마다 각각 하나여야 합니다.
- 선언 형식, 참조 범위, 헤더·타입 행 위치가 규약과 다르면 변환을 중단합니다.
- 각 타입 행 셀은 대응하는 헤더 필드의 타입이어야 합니다.
- 모든 데이터 셀은 선언한 Protocol Buffers 타입으로 변환 가능해야 합니다.
- pk 필드가 없거나 둘 이상이면 변환을 중단합니다.
- PK 값은 누락되거나 중복될 수 없습니다.

## Protocol Buffers 필드 번호

Protocol Buffers 필드 번호는 #Range의 헤더 열을 왼쪽에서 오른쪽으로 읽어 1부터 순서대로 자동 생성합니다. pk와 op 속성 여부는 필드 번호 순서에 영향을 주지 않습니다.

열의 삽입·삭제·이동은 이후 필드 번호를 바꾸므로, 기존 Payload BLOB과의 호환성을 깨뜨립니다. 이 프로젝트에서는 Excel을 변경한 뒤 SQLite 데이터베이스와 모든 Payload를 다시 생성하는 것을 전제로 합니다.
