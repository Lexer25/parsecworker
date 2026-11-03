# parsecworker

Модуль интеграции СКУД Артонит и СКУД Парсек (.NET 8, служба Windows).

Документация службы: [ParsecIntegrationClient/README.md](ParsecIntegrationClient/README.md).

Монитор очереди для интегратора: [ParsecQueueViewer/README.md](ParsecQueueViewer/README.md).
Это отдельное desktop-приложение, которое читает `appsettings.json` службы и
показывает `PARSEC_QUEUE` / `PARSEC_QUEUE_LOG` без изменения данных.

Есть два варианта монитора:

- WPF-вариант — [ParsecQueueViewer](ParsecQueueViewer), один автономный exe ≈72 МБ
  `ParsecQueueViewerWpf.exe`: .NET 8 и WPF внутри, на другом компьютере ничего
  доустанавливать не нужно. Обновляется по событию Firebird `PARSEC_QUEUE_CHANGED`
  (триггер из `db/08_parsec_queue_events.sql`), резервный опрос — только как страховка.
  Сборка одного файла: `build-wpf.cmd`.
- Win32-вариант — [ParsecQueueViewer.Win32](ParsecQueueViewer.Win32), один exe ≈1,3 МБ
  плюс внешний `fbclient.dll` ≈348 КБ (Win32 API + .NET Framework 4.8, без WPF/WinForms).
  Обновляется по событию Firebird `PARSEC_QUEUE_CHANGED` (триггер из
  `db/08_parsec_queue_events.sql`), резервный опрос — только как страховка.
  Сборка: `build-win32.cmd`.

История версий: [history.txt](history.txt).

Сборка и установщик WPF-варианта: `build.cmd`.
