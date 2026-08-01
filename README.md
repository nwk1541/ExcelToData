# ExcelToData

Excel로 작성한 게임 기획 데이터를 검증한 뒤, Protocol Buffers Payload를 포함한 SQLite 데이터베이스로 변환하는 테스트 프로젝트입니다.

## 변환 흐름

~~~text
Excel Workbook
  → 규약 검증
  → Protocol Buffers 직렬화
  → SQLite 생성
~~~

## 핵심 규약

| 항목 | 규칙 |
| --- | --- |
| 워크시트 | 워크시트 하나는 SQLite 테이블 하나에 대응하며, 시트 이름을 테이블 이름으로 사용합니다. |
| #Range | 워크시트마다 정확히 하나를 선언합니다. 대상 범위 밖의 셀에 두며, 헤더·타입·데이터 행을 포함하는 연속된 직사각형 범위를 지정합니다. |
| #Type | 워크시트마다 정확히 하나를 선언합니다. 타입 행의 첫 셀을 지정하며, 해당 행은 헤더 바로 다음 행이고 #Range와 같은 열 폭을 가져야 합니다. |
| 헤더 | 필드명 또는 필드명:속성 형식입니다. 필드명은 비어 있거나 중복될 수 없습니다. |
| 데이터 | 타입 행 바로 다음 행부터 시작합니다. 각 데이터 행 전체는 Protocol Buffers로 직렬화되어 Payload BLOB에 저장됩니다. |

~~~text
A2: #Range=C2:G6
A3: #Type=C3

C2: id:pk    D2: type:op    E2: stat_id    F2: skill_ids         G2: name
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
| 필드명 | Payload에만 저장합니다. |
| 필드명:pk | 모든 워크시트에 정확히 하나가 필수입니다. int32, int64, string 같은 단일 값 타입만 허용하며, 값은 비어 있거나 중복될 수 없습니다. SQLite PK 컬럼과 Payload에 저장됩니다. |
| 필드명:op | SQLite 독립 NOT NULL 컬럼과 단일 컬럼 인덱스를 생성합니다. 값은 Payload에도 함께 저장됩니다. repeated\<T\>와 중첩 메시지 타입에는 지정할 수 없습니다. |

## 자료형

자료형은 Protocol Buffers 호환 논리 타입을 기준으로 작성합니다. pk 또는 op 필드에만 SQLite 타입 매핑을 적용합니다.

| Excel 타입 | SQLite 타입 |
| --- | --- |
| int32, int64 | INTEGER |
| float, double | REAL |
| bool | INTEGER (0 또는 1) |
| string | TEXT |
| bytes | BLOB |
| repeated\<T\> | 지원하지 않음 (Payload 전용) |

- 목록 셀은 쉼표(,)로 값을 구분하고 각 값의 앞뒤 공백을 제거합니다.
- string, repeated\<T\> 타입은 각각 빈 문자열과 빈 목록으로 처리하며 나머지 모든 타입 의 빈 셀은 변환 오류입니다.

## 변환 실패 조건

- #Range 또는 #Type 선언이 없거나 둘 이상인 경우
- 선언 형식, 참조 범위, 헤더·타입 행 위치가 규약과 다른 경우
- 타입 행과 헤더의 열 폭이 다르거나, 데이터 셀을 선언한 타입으로 변환할 수 없는 경우
- string과 repeated\<T\>를 제외한 타입에 빈 셀이 있는 경우
- pk 필드가 없거나 둘 이상인 경우, 또는 PK 값이 누락·중복된 경우

## Protocol Buffers 필드 번호

필드 번호는 #Range의 헤더 열을 왼쪽에서 오른쪽으로 읽어 1부터 순서대로 자동 생성합니다. pk와 op 속성은 번호 순서에 영향을 주지 않습니다.

열을 삽입·삭제·이동하면 이후 필드 번호가 바뀌므로 기존 Payload BLOB과 호환되지 않습니다. Excel 구조를 변경한 뒤에는 SQLite 데이터베이스와 모든 Payload를 다시 생성합니다.
