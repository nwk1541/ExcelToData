# SQLite 런타임 DLL 출처

아래 NuGet 패키지의 바이너리를 수정 없이 포함합니다. 관리 DLL은 `lib/netstandard2.0`, 네이티브 DLL은 `runtimes/win-x64/native`에서 가져왔습니다. NuGet 패키지 전체나 DataExporter의 빌드 출력물을 복사하지 않았습니다.

| 포함 파일 | NuGet 패키지 | 버전 | 라이선스 |
| --- | --- | --- | --- |
| `Runtime/Plugins/Managed/Microsoft.Data.Sqlite.dll` | [Microsoft.Data.Sqlite.Core](https://www.nuget.org/packages/Microsoft.Data.Sqlite.Core/10.0.10) | 10.0.10 | MIT |
| `Runtime/Plugins/Managed/SQLitePCLRaw.core.dll` | [SQLitePCLRaw.core](https://www.nuget.org/packages/SQLitePCLRaw.core/2.1.11) | 2.1.11 | Apache-2.0 |
| `Runtime/Plugins/Managed/SQLitePCLRaw.provider.e_sqlite3.dll` | [SQLitePCLRaw.provider.e_sqlite3](https://www.nuget.org/packages/SQLitePCLRaw.provider.e_sqlite3/2.1.11) | 2.1.11 | Apache-2.0 |
| `Runtime/Plugins/Windows/x86_64/e_sqlite3.dll` | [SQLite](https://www.nuget.org/packages/SQLite/3.53.4) | 3.53.4 | Public Domain |

## 저작권 및 라이선스 원문

- Microsoft.Data.Sqlite: Copyright (c) .NET Foundation and Contributors. NuGet 표기: © Microsoft Corporation. All rights reserved. [MIT 원문](<Third Party Licenses/Microsoft.Data.Sqlite.LICENSE.txt>), [원본 저장소](https://github.com/dotnet/efcore/blob/v10.0.10/LICENSE.txt).
- SQLitePCLRaw: Copyright 2014-2024 SourceGear, LLC. [Apache-2.0 원문](<Third Party Licenses/SQLitePCLRaw.LICENSE.txt>), [원본 저장소](https://github.com/ericsink/SQLitePCL.raw/blob/v2.1.11/LICENSE.TXT).
- SQLite: [패키지 라이선스 고지](<Third Party Licenses/SQLite.LICENSE.txt>), [SQLite 저작권 설명](https://sqlite.org/copyright.html).

## 의존성 선택과 초기화

- `Microsoft.Data.Sqlite.Core` 10.0.10의 .NET Standard 2.0 의존성은 `SQLitePCLRaw.core >= 2.1.11`입니다.
- `SQLitePCLRaw.core` 2.1.11은 `System.Memory >= 4.5.3`을 요구합니다. 이 프로젝트에서는 기존 `org.nuget.google.protobuf` 3.35.1의 UPM 의존성으로 `org.nuget.system.memory` 4.5.3과 관련 DLL이 이미 공급됩니다. 같은 DLL을 플러그인 폴더에 중복 복사하지 않습니다.
- DataExporter의 SQLitePCLRaw 3.0.5 대신 2.1.11을 선택해 기존 Unity의 System.Memory 의존성을 유지했습니다. 두 프로그램이 사용하는 SQLite 엔진은 3.53.4입니다.
- `bundle` 및 `Batteries_V2`는 포함하지 않습니다. 연결을 열기 전에 `SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3())`를 호출합니다. 현재 호출 위치는 프로젝트의 `SqliteValidation`이며, `SqlManager` 구현 시 패키지 초기화로 옮깁니다.
- 모든 포함 DLL의 Unity 적용 대상은 Windows Editor와 Windows x64 Player입니다. 네이티브 DLL은 OS `Windows`, CPU `x86_64`로 제한합니다. Android·iOS 등 다른 플랫폼의 구성은 아직 없습니다.
- 이 DLL 조합의 확인 범위는 Unity 6000.5.2f1 Windows Editor의 실제 Play 실행입니다. Windows Player의 Mono·IL2CPP 빌드 성공을 의미하지 않습니다.

## SHA-256

```text
Microsoft.Data.Sqlite.dll
E3CA42644CE012C350EF84675F399A9164D6B3492BC9A435AA7E75AE27FF1DB5

SQLitePCLRaw.core.dll
3D2ED8E186F124F988EBDB45D0354185B424357BE2433BBA0033AB9EC31BD25B

SQLitePCLRaw.provider.e_sqlite3.dll
25D2FA721D1504A6FBD31B4CD6457D93CE88BDD3B61AF81D644F931C21DD18D5

e_sqlite3.dll
6AD8E149F8CE3ED3716402B4B3A2268EBBDC7B64391B5FAFED747E03BB1B9418
```
