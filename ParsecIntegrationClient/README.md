# ParsecIntegrationClient

Модуль интеграции СКУД Артонит (Firebird/ShieldPro) с СКУД Парсек по SOAP-сервису
`IntegrationService`. Приложение — Worker на `Microsoft.Extensions.Hosting`: один exe
работает и из консоли, и как служба Windows.

Текущая версия — **3.0.0** на **.NET 8** (`net8.0-windows`, win-x64).

## Что изменилось в 3.0.0

| Было (2.0.1) | Стало (3.0.0) |
|---|---|
| `net472` (.NET Framework 4.7.2) | `net8.0-windows`, self-contained |
| SOAP-прокси `Web References/.../Reference.cs` на `System.Web.Services` | WCF-клиент `System.ServiceModel` (BasicHttpBinding), сгенерирован `dotnet-svcutil` из того же WSDL |
| Fody/Costura, затем самодельное встраивание DLL в ресурсы exe (`EmbeddedAssemblies.cs`) | Штатный `PublishSingleFile` .NET 8, без сторонних библиотек и без `AssemblyResolve` |
| Сборка одного exe вручную + `--verify-embedded` | Один exe даёт `dotnet publish`; проверка запуска — `ParsecIntegrationClient.exe --version` |
| Пакеты `Microsoft.Extensions.*` 9.0.2 | Без изменений (работают на .NET 8) |

Логика интеграции (очередь `cardindev`, операции с людьми, организациями, картами и
группами доступа), формат журнала, `state.json`, имена службы и настроек сохранены.

## Структура

```
ParsecIntegrationClient.csproj   Microsoft.NET.Sdk.Worker, net8.0-windows
Program.cs                       сборка хоста, конфигурация, журналирование, старт/стоп
Worker.cs                        основной цикл: авторизация в Парсеке + обработка очереди
SessionKeepAliveWorker.cs        продление сессии Парсека
Options/WorkerOptions.cs         секции appsettings.json (Paths/Log/Parsec/Service)
IntegrationWebService/
    IntegrationService.wsdl      исходный WSDL Парсека (для перегенерации)
    IntegrationService.cs        сгенерированный WCF-клиент и модели (не править вручную)
Services/Paths.cs                рабочие каталоги службы
Services/ParsecClient.cs         фабрика SOAP-клиента: адрес, таймаут, binding
Services/Logger.cs               фасад Logger.Log<T> поверх ILogger
Services/Logging/                файловый журнал в прежнем формате
Services/QueueProcessor.cs       обработка очереди cardindev (бывший MainJob)
Services/ParsecService.cs        операции с Парсеком
Services/DatabaseService.cs      работа с БД Артонит
Services/StateService.cs         state.json
Services/SettingsService.cs      настройки + совместимость со старым appsettings.json
Models/                          модели БД, состояния и результатов операций
Properties/PublishProfiles/      профиль публикации в папку
tools/regenerate-parsec-client.ps1   перегенерация SOAP-клиента из WSDL
```

В корне репозитория: `build.cmd` (сборка + публикация + установщик) и `ParsecSetup.iss`.

## Требования

- **Сборка:** .NET SDK 8.0 или новее (проверено на SDK 10.0.401), Windows.
  Первый `restore` и первый запуск скрипта перегенерации требуют доступа к nuget.org.
- **Стенд:** Windows x64. Отдельно ставить .NET 8 не нужно: поставка self-contained.
  Нужны только права на запись в `Paths:MainPath` для учётной записи службы.

## Сборка и запуск

Из корня репозитория (`C:\ParsecIntegrationClinet_worker`):

```powershell
dotnet build .\ParsecIntegrationClient\ParsecIntegrationClient.csproj

# консоль: то же самое exe, никаких ключей не нужно
.\ParsecIntegrationClient\bin\Debug\net8.0-windows\win-x64\ParsecIntegrationClient.exe
```

Остановка в консоли — `Ctrl+C`: приложение закрывает сессию Парсека и завершается штатно.
Проверка, что exe запускается (Парсек и база не нужны):

```powershell
.\ParsecIntegrationClient.exe --version      # печатает 3.0.0.0
```

## Публикация и установщик

```powershell
# 1) всё сразу: restore -> build -> publish -> установщик
.\build.cmd

# 2) только папка поставки, через профиль публикации
dotnet publish .\ParsecIntegrationClient\ParsecIntegrationClient.csproj -p:PublishProfile=FolderProfile

# 3) вручную
dotnet publish .\ParsecIntegrationClient\ParsecIntegrationClient.csproj -c Release -o .\publish
```

`build.cmd` создаёт:

- `publish\` — `ParsecIntegrationClient.exe` (≈38 МБ, все зависимости внутри),
  `appsettings.json`, `install-service.ps1`, `uninstall-service.ps1`, `README.md`, `*.pdb`;
- `Output\ParsecIntegrationSetup_v3.0.0.0.exe` — установщик (Inno Setup 6, `ParsecSetup.iss`).

### Поставка из одного файла

В csproj включены `PublishSingleFile`, `SelfContained`, `IncludeNativeLibrariesForSelfExtract`
и `EnableCompressionInSingleFile`. Никакого Costura/Fody и никакого собственного загрузчика
сборок нет: всё делает SDK .NET 8. Отдельных DLL рядом с exe не остаётся.

`RuntimeIdentifier` зафиксирован как `win-x64`. Если на стендах уже установлен .NET 8,
можно собрать компактную framework-dependent поставку (единицы МБ вместо десятков):

```powershell
dotnet publish .\ParsecIntegrationClient\ParsecIntegrationClient.csproj -c Release -o .\publish `
  -p:SelfContained=false -p:PublishSingleFile=true
```

Trimming намеренно не включён: `XmlSerializer` (SOAP), Firebird и конфигурация используют
рефлексию, IL-обрезка для них небезопасна.

## SOAP-клиент Парсека

`IntegrationWebService\IntegrationService.cs` — WCF-клиент (`System.ServiceModel.Http` 8.0.0),
сгенерированный из `IntegrationService.wsdl` командой:

```
dotnet-svcutil IntegrationService.wsdl
    --outputDir ParsecIntegrationClient\IntegrationWebService
    --outputFile IntegrationService.cs
    --namespace *,ParsecIntegrationClient.IntegrationWebService
    --serializer XmlSerializer
    --syncOnly
```

- `XmlSerializer` обязателен: WSDL Парсека — document/literal, как у ASMX;
- `--syncOnly` сохраняет синхронные методы, потому что `ParsecService` вызывает операции
  синхронно (асинхронные обёртки не нужны);
- все типы остаются в прежнем пространстве имён `ParsecIntegrationClient.IntegrationWebService`,
  поэтому модели и код операций не переписывались;
- используется SOAP 1.1 (`BasicHttpBinding`), как и прежний `SoapHttpClientProtocol`.

Перегенерация (например, после обновления Парсека) — скриптом:

```powershell
.\tools\regenerate-parsec-client.ps1
```

Скрипт сам ставит `dotnet-svcutil` 8.0.0 в локальный `.tools` (каталог в `.gitignore`),
генерирует файл во временный каталог и заменяет `IntegrationService.cs` только после
успешной генерации. Адрес сервиса в сгенерированном коде — лишь значение из WSDL; в работе
используется `Parsec:Url` из `appsettings.json` (см. `Services/ParsecClient.cs`).

## Установка как служба

```powershell
dotnet publish .\ParsecIntegrationClient\ParsecIntegrationClient.csproj -c Release -o .\publish
cd .\publish
.\install-service.ps1
.\uninstall-service.ps1   # полное удаление
```

Имя службы прежнее — `ParsecIntegrationService`, поэтому обновление на стенде выполняется
поверх старой версии: остановить службу, заменить файлы содержимым `publish`, запустить
`install-service.ps1` (он обновит путь к exe и запустит службу).

Скрипт создаёт службу с `start= auto`, описанием и перезапуском при падении (3 попытки
с интервалом 60 с). Учётная запись по умолчанию — `NT AUTHORITY\LocalService`; другую можно
указать параметрами `-Account` и `-Password`. Этой учётной записи нужны права на запись
в `Paths:MainPath`.

Установщик Inno Setup ставит файлы в `C:\Program Files\ParsecIntegrationService`,
регистрирует службу, настраивает перезапуск и запускает её. При обновлении поверх
существующей версии служба останавливается, файлы заменяются, а `appsettings.json`
**не перезаписывается**. При удалении служба снимается, настройки и данные
(`C:\IntegrationClient`) остаются.

## Настройки

Единственный файл — `appsettings.json` рядом с exe.

| Ключ | Назначение | Прежнее значение |
|---|---|---|
| `Paths:MainPath` | рабочий каталог (журнал, state.json) | `C:\IntegrationClient` |
| `Log:Level` | уровень журнала приложения | `_logLevel` = `Warning` |
| `Log:FrameworkLevel` | уровень категорий `Microsoft.*`/`System.*` | — |
| `Log:FolderPath` | каталог журнала, пусто = `<MainPath>\log` | — |
| `Log:RetainedFileCount` | сколько файлов журнала хранить | 31 |
| `Parsec:Url` | адрес SOAP-сервиса | был зашит в прокси |
| `Parsec:Domain/User/Password` | учётная запись интеграции | были зашиты в коде |
| `Parsec:TimeoutSeconds` | таймаут SOAP-запроса (Open/Send/Receive/Close) | `Timeout` прокси, 100 с |
| `Parsec:SessionContinueSeconds` | период продления сессии | 240 с (Quartz) |
| `Parsec:SessionRetrySeconds` | пауза между попытками авторизации | — |
| `Service:DatabaseConnectionString` | строка подключения к Артонит (**обязательна**) | `_databaseConnectionString` |
| `Service:DatabaseRetrySeconds` | пауза между проверками БД, секунды | — |
| `Service:JobIntervalSeconds` | интервал обработки очереди | 600 с (Quartz) |
| `Service:ErrorTimeoutMinutes` | пауза после ошибки | `_errorTimeoutMinutes` |
| `Service:SkipErrCode` | коды ошибок, удаляющие задание | `_skipErrCode` |

Строка подключения к Firebird обычно содержит `Charset=win1251`; провайдер кодовых страниц
в .NET 8 регистрируется в `Program.Main`.

### Проверка базы данных

`Worker` проверяет подключение к Артонит **до** авторизации в Парсеке и перед каждой итерацией:

- подключение не установлено → в журнал пишется `Critical`
  «Нет подключения к базе данных Артонит: <причина>. Строка подключения: <источник>»,
  очередь не обрабатывается, повтор через `Service:DatabaseRetrySeconds`;
- строка подключения пуста и в прежнем файле её нет → тот же `Critical` с причиной
  «строка подключения не задана».

Встроенной строки подключения в коде больше нет: пустая настройка — явная ошибка в журнале,
а не молчаливая подстановка чужого пути.

### Совместимость со старой установкой

Если рядом со службой остался прежний файл `C:\IntegrationClient\appsettings.json`
с ключами `_databaseConnectionString`, `_databaseJobTimeout`, `_errorTimeoutMinutes`,
`_skipErrCode`, `_logLevel`, его значения **имеют приоритет** — после обновления служба
ведёт себя как раньше. В журнал при старте пишется предупреждение с перечнем использованных
ключей. Порядок миграции:

1. перенести значения из старого файла в `appsettings.json` рядом с exe (секции `Service`/`Log`);
2. удалить `C:\IntegrationClient\appsettings.json`;
3. перезапустить службу.

`state.json` и журнал остаются в `Paths:MainPath` — менять ничего не нужно.

## Журнал

Файл `Log_At_ГГГГ_М_Д.txt` в `Log:FolderPath`, строка прежнего формата:

```
21.6.2026 14:30:0 LOG: Warning-QueueProcessor Message: 21 ЗАПУСК ОБРАБОТКИ ЗАДАЧ
```

Уровень `Exception` пишется как `Error` (в `ILogger` такого уровня нет), в конце строки
выводится стек исключения. Пароль из строки подключения в журнал не попадает — маскируется.

При успешном подключении к Парсеку в журнал пишутся две строки уровня `Warning`:

```
... LOG: Warning-Worker Message: Подключение к SOAP Парсека установлено: http://.../IntegrationService.asmx; протокол SOAP: SOAP 1.1; сессия: 1111...
... LOG: Warning-Worker Message: Версия SOAP-сервиса Парсека: 3.1.2.777
```

Версия берётся методом `GetVersion`; если он не ответил, пишется предупреждение с причиной,
но работа продолжается.
