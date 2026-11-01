# parsecworker

Модуль интеграции СКУД Артонит и СКУД Парсек (.NET 8, служба Windows).

Документация службы: [ParsecIntegrationClient/README.md](ParsecIntegrationClient/README.md).

Монитор очереди для интегратора: [ParsecQueueViewer/README.md](ParsecQueueViewer/README.md).
Это отдельное desktop-приложение, которое читает `appsettings.json` службы и
показывает `PARSEC_QUEUE` / `PARSEC_QUEUE_LOG` без изменения данных.

Есть два варианта монитора:

- WPF-вариант — [ParsecQueueViewer](ParsecQueueViewer), один self-contained exe ≈69 МБ (.NET 8);
- Win32-вариант — [ParsecQueueViewer.Win32](ParsecQueueViewer.Win32), один exe ≈1,3 МБ
  плюс внешний `fbclient.dll` ≈348 КБ (Win32 API + .NET Framework 4.8, без WPF/WinForms).
  Сборка: `build-win32.cmd`.

История версий: [history.txt](history.txt).

Сборка и установщик WPF-варианта: `build.cmd`.
