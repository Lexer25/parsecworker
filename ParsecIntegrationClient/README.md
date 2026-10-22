# ParsecIntegrationClient

Модуль интеграции СКУД Артонит (Firebird/ShieldPro) с СКУД Парсек по SOAP-сервису
`IntegrationService`. Схема приложения — как в `basip_exe`: обычный Worker на
`Microsoft.Extensions.Hosting`, один exe работает и из консоли, и как служба Windows.

## Что изменилось по сравнению с прежней версией

| Было | Стало |
|---|---|
| `Service1 : ServiceBase` + `ProjectInstaller` + `installutil` | `UseWindowsService()`, установка скриптом `install-service.ps1` |
| Ручное ветвление `Main`: `/console`, `Environment.UserInteractive`, `ServiceBase.Run` | Режим определяется автоматически, ключи не нужны |
| Планировщик Quartz 3.6 (2 задания + триггеры) | Два `BackgroundService`: `Worker` и `SessionKeepAliveWorker` |
| Fody/Costura: сборка DLL внутрь exe, папка `packages` | Один exe тем же способом (ресурсы + `AssemblyResolve`), но без Fody и без `packages` |
| Свои `SettingsService`/`Logger` на статике и Json.NET | Конфигурация .NET + `ILogger`; фасады `SettingsService`/`Logger` сохранены для существующего кода |
| Guardant SDK7, проверка ключа (была закомментирована) | Убрано, лицензирование отключено |
| Путь `C:\IntegrationClient` зашит в `Service1.MainPath` | `Paths:MainPath` в `appsettings.json` |

## Структура

```
ParsecIntegrationClient.csproj   Microsoft.NET.Sdk.Worker, net472
Program.cs                       сборка хоста, конфигурация, журналирование, старт/стоп в журнале
Worker.cs                        основной цикл: авторизация в Парсеке + обработка очереди
SessionKeepAliveWorker.cs        продление сессии Парсека
Options/WorkerOptions.cs         секции appsettings.json (Paths/Log/Parsec/Service)
EmbeddedAssemblies.cs            загрузчик сборок, встроенных в exe (поставка из одного файла)
Services/Paths.cs                рабочие каталоги службы
Services/Logger.cs               фасад Logger.Log<T> поверх ILogger
Services/Logging/                файловый журнал в прежнем формате
Services/QueueProcessor.cs       обработка очереди cardindev (бывший MainJob)
Services/ParsecService.cs        операции с Парсеком (без изменений)
Services/DatabaseService.cs      работа с БД Артонит (без изменений)
Services/StateService.cs         state.json (без изменений)
Services/SettingsService.cs      настройки + совместимость со старым appsettings.json
Models/, Web References/         модели и SOAP-прокси Парсека (без изменений)
Properties/PublishProfiles/      профиль публикации в папку (dotnet publish -p:PublishProfile)
```

В корне репозитория: `build.cmd` (сборка + публикация + установщик) и `ParsecSetup.iss` (Inno Setup).

## Запуск

Из корня репозитория (`C:\ParsecIntegrationClinet_worker`):

```powershell
dotnet build .\ParsecIntegrationClient\ParsecIntegrationClient.csproj

# консоль: то же самое exe, никаких ключей не нужно
.\ParsecIntegrationClient\bin\Debug\net472\ParsecIntegrationClient.exe
```

Остановка в консоли — `Ctrl+C`: приложение закрывает сессию Парсека и завершается штатно.

## Установка как служба

```powershell
dotnet publish .\ParsecIntegrationClient\ParsecIntegrationClient.csproj -c Release -o .\publish
cd .\publish
.\install-service.ps1
.\uninstall-service.ps1   # полное удаление
```

Имя службы сохранено прежним — `ParsecIntegrationService`, поэтому обновление на стенде
выполняется поверх старой версии: остановить службу, заменить файлы содержимым `publish`,
запустить `install-service.ps1` (он обновит путь к exe и запустит службу).

Скрипт создаёт службу с `start= auto`, описанием и перезапуском при падении
(3 попытки с интервалом 60 с). Учётная запись по умолчанию — `NT AUTHORITY\LocalService`,
как в прежнем `ProjectInstaller`; другую можно указать параметрами `-Account` и `-Password`
(для доменной учётной записи). Этой учётной записи нужны права на запись в `Paths:MainPath`.

В папку `publish` попадают exe, все зависимости, `appsettings.json`, README и оба скрипта —
её можно целиком копировать на стенд.

## Публикация и установщик

В Visual Studio пункта «Публиковать…» у этого проекта нет — и это ожидаемо. В прежней версии
это был ClickOnce-мастер (в csproj `PublishUrl`, `Install`, `ApplicationVersion`, ключ `.pfx`;
на выходе `setup.exe` + `Application Files`), а ClickOnce к проектам на новом SDK-формате
не применяется. Для net472 Visual Studio не показывает и «папочную» публикацию, хотя
MSBuild-публикация работает полностью.

Публикация выполняется так:

```powershell
# 1) всё сразу: restore -> build -> publish -> установщик
.\build.cmd

# 2) только папка поставки, через профиль публикации
dotnet publish .\ParsecIntegrationClient\ParsecIntegrationClient.csproj -p:PublishProfile=FolderProfile

# 3) вручную
dotnet publish .\ParsecIntegrationClient\ParsecIntegrationClient.csproj -c Release -o .\publish
```

`build.cmd` создаёт:

- `publish\` — один exe со всеми зависимостями внутри, `appsettings.json`, скрипты, README;
- `Output\ParsecIntegrationSetup_v1.0.5.45.exe` — установщик (Inno Setup 6, `ParsecSetup.iss`).

### Поставка из одного файла

На .NET Framework нет `PublishSingleFile` — это механизм .NET Core. Поэтому зависимости
встраиваются в exe как ресурсы, как раньше делал Costura, но без Fody и сторонних библиотек:

- MSBuild-target `EmbedReferencedAssemblies` (в csproj) складывает в ресурсы все DLL из
  `ReferenceCopyLocalPaths`, а `TrimPublishToSingleExe` убирает их из папки публикации;
- `EmbeddedAssemblies.cs` подписывается на `AppDomain.AssemblyResolve` и отдаёт сборки
  из памяти; вызов стоит первой строкой `Main`, до обращения к типам `Microsoft.Extensions.*`
  (иначе JIT ищет их файлами рядом с exe).

Внутри exe (≈4,9 МБ) лежат все 41 зависимость: `FirebirdSql.Data.FirebirdClient`,
`Newtonsoft.Json`, `Microsoft.Extensions.*`, `System.Text.Json` и остальные. Рядом
достаточно положить `appsettings.json`; `exe.config`, `.pdb` и скрипты — необязательные
служебные файлы, без них exe тоже запускается.

Проверка целостности поставки на стенде (Парсек и база не нужны):

```powershell
.\ParsecIntegrationClient.exe --verify-embedded
```

Команда печатает список встроенных сборок, грузит каждую и перечисляет типы.
Код возврата: `0` — всё загрузилось, `1` — есть проблемы.

Версия установщика берётся из файла `publish\ParsecIntegrationClient.exe`, то есть из `<Version>`
в csproj — править её в двух местах не нужно.

Установщик ставит файлы в `C:\Program Files\ParsecIntegrationService`, регистрирует службу
`ParsecIntegrationService` от `NT AUTHORITY\LocalService`, настраивает перезапуск при падении
и запускает её. При обновлении поверх существующей версии служба останавливается, файлы
заменяются, а `appsettings.json` **не перезаписывается**. При удалении служба снимается,
настройки и данные (`C:\IntegrationClient`) остаются на месте.

Пункт «Публиковать…» в Visual Studio (в том числе публикация одним файлом) возможен только
после перехода на .NET 8 — см. раздел ниже.

## Настройки

Единственный файл — `appsettings.json` рядом с exe.

| Ключ | Назначение | Прежнее значение |
|---|---|---|
| `Paths:MainPath` | рабочий каталог (журнал, state.json) | `C:\IntegrationClient` |
| `Log:Level` | уровень журнала приложения | `_logLevel` = `Warning` |
| `Log:FolderPath` | каталог журнала, пусто = `<MainPath>\log` | — |
| `Parsec:Url` | адрес SOAP-сервиса | был зашит в прокси |
| `Parsec:Domain/User/Password` | учётная запись интеграции | были зашиты в коде |
| `Parsec:SessionContinueSeconds` | период продления сессии | 240 с (Quartz) |
| `Service:DatabaseConnectionString` | строка подключения к Артонит (**обязательна**) | `_databaseConnectionString` |
| `Service:DatabaseRetrySeconds` | пауза между проверками БД, секунды | — |
| `Service:JobIntervalSeconds` | интервал обработки очереди | 600 с (Quartz) |
| `Service:ErrorTimeoutMinutes` | пауза после ошибки | `_errorTimeoutMinutes` |
| `Service:SkipErrCode` | коды ошибок, удаляющие задание | `_skipErrCode` |

### Проверка базы данных

`Worker` проверяет подключение к Артонит **до** авторизации в Парсеке и перед каждой итерацией:

- подключение не установлено → в журнал пишется `Critical`
  «Нет подключения к базе данных Артонит: <причина>. Строка подключения: <источник>»,
  очередь не обрабатывается, повтор через `Service:DatabaseRetrySeconds`;
- строка подключения пуста и в прежнем файле её нет → тот же `Critical` с причиной
  «строка подключения не задана», встроенного значения в коде больше нет.

Встроенной строки подключения (как раньше `User=SYSDBA;...ShieldPro_rest.gdb`) больше нет:
пустая настройка теперь означает явную ошибку в журнале, а не молчаливую подстановку чужого пути.

### Совместимость со старой установкой

Если рядом со службой остался прежний файл `C:\IntegrationClient\appsettings.json`
с ключами `_databaseConnectionString`, `_databaseJobTimeout`, `_errorTimeoutMinutes`,
`_skipErrCode`, `_logLevel`, его значения **имеют приоритет** — служба после обновления
ведёт себя как раньше. В журнал при старте пишется предупреждение с перечнем
использованных ключей. Порядок миграции:

1. перенести значения из старого файла в `appsettings.json` рядом с exe (секции `Service`/`Log`);
2. удалить `C:\IntegrationClient\appsettings.json`;
3. перезапустить службу.

`state.json` и журнал остаются в `Paths:MainPath` — менять ничего не нужно.

## Журнал

Файл `Log_At_ГГГГ_М_Д.txt` в `Log:FolderPath`, строка прежнего формата:

```
21.6.2026 14:30:0 LOG: Warning-QueueProcessor Message: 21 ЗАПУСК ОБРАБОТКИ ЗАДАЧ
```

Отличие одно: уровень `Exception` теперь пишется как `Error` (в `ILogger` такого уровня нет),
плюс в конце строки выводится стек исключения.

Пароль из строки подключения в журнал не попадает — маскируется.

При успешном подключении к Парсеку в журнал пишутся две строки уровня `Warning`
(видны при уровне по умолчанию):

```
... LOG: Warning-Worker Message: Подключение к SOAP Парсека установлено: http://.../IntegrationService.asmx; протокол SOAP: SOAP 1.1 (по умолчанию); сессия: 1111...
... LOG: Warning-Worker Message: Версия SOAP-сервиса Парсека: 3.1.2.777
```

Версия берётся методом `GetVersion` сервиса интеграции; если он не ответил, пишется
предупреждение с причиной, но работа продолжается.

## Переход на .NET 8

Отдельная задача: SOAP-прокси Парсека (`Web References/.../Reference.cs`, 555 КБ) собран
под `System.Web.Services`, которого в .NET 8 нет. Понадобится перегенерация клиента
(`dotnet-svcutil` → `System.ServiceModel.Http`) и проверка на стенде Парсека. Всё остальное
(хостинг, конфигурация, журналирование, обработка очереди) к платформе не привязано.







