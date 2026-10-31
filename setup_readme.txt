================================================================================
 Parsec Integration Client — памятка по установке
================================================================================

Файл описывает три действия после установки:

  1) выполнение SQL-скриптов из папки db;
  2) установку службы Windows через sc.exe;
  3) удаление и переименование службы через sc.exe.

Все команды sc.exe и isql выполняются в командной строке, запущенной
от имени администратора. В PowerShell вызывайте именно sc.exe: короткое имя sc
там занято алиасом Set-Content.


================================================================================
 1. ПОСЛЕ УСТАНОВКИ: SQL-СКРИПТЫ ИЗ ПАПКИ db
================================================================================

Установщик не изменяет схему базы Артонит: у него нет на это прав. Сразу после
установки, ДО первого запуска службы, примените к базе скрипты из папки db.
Если служба уже запущена — сначала остановите её:

    sc stop ParsecIntegrationService

Скрипты выполняются под SYSDBA через isql (входит в поставку Firebird).
Путь к базе и пароль замените на свои.

  Чистая база (PARSEC_QUEUE ещё нет) — строго в таком порядке:

    isql -user SYSDBA -password <пароль> localhost:D:\path\to\database.gdb -i db\01_parsec_queue_ddl.sql
    isql -user SYSDBA -password <пароль> localhost:D:\path\to\database.gdb -i db\02_parsec_enqueue.sql
    isql -user SYSDBA -password <пароль> localhost:D:\path\to\database.gdb -i db\03_parsec_queue_triggers.sql

  Обновление базы, где уже выполнены скрипты 01..03 версии 4.0.0:

    isql -user SYSDBA -password <пароль> localhost:D:\path\to\database.gdb -i db\06_parsec_queue_card_11.sql

  Дополнительные скрипты (по необходимости):

    db\04_backfill_from_cardindev.sql  — разовый перенос незавершённых операций
                                         3..10, 35, 55 из CARDINDEV. Если такие
                                         операции есть, выполните 04 после 01 и 02,
                                         но ДО 03 (до включения новых триггеров),
                                         иначе backfill создаст дубли.
    db\05_retention.sql                — очистка завершённых событий старше 12 месяцев.
                                         Запускать по расписанию, не из службы.
    db\99_drop_parsec_queue.sql        — удаляет всё созданное, вместе с историей.
                                         Только для тестовой базы!

После применения скриптов запустите службу:

    sc start ParsecIntegrationService

Важно:

  * Скрипт 03 пересоздаёт триггеры через drop + create. Firebird фиксирует каждый
    DDL сразу, поэтому в момент между drop и create триггера нет. Останавливайте
    службу на время выполнения 03 и 06, чтобы не потерять события.
  * Firebird 1.5 не знает "drop trigger if exists": при первом запуске 03 isql
    напечатает "Trigger not found", продолжит работу и завершится с кодом 1.
    Это ожидаемо — все триггеры будут созданы. Не запускайте файл с ключом isql -b.
  * Файлы скриптов в UTF-8, база в WIN1251. Русский текст в ERROR_TEXT может
    сохраниться в UTF-8; на работу службы это не влияет. При необходимости
    перекодируйте скрипт в WIN1251 перед запуском (IBExpert, FlameRobin) или
    применяйте клиентом с явно заданной кодировкой.

Проверка после установки (запросы можно выполнить в isql или IBExpert):

    select count(*) from parsec_queue;

    select rdb$trigger_name from rdb$triggers
     where rdb$trigger_name starting with 'PARSEC_Q_';

Должно вернуться 11 триггеров PARSEC_Q_*.

Если служба работает не под SYSDBA, выдайте ей права:

    grant select, update on parsec_queue to <service_user>;
    grant select, insert on parsec_queue_log to <service_user>;

Триггеры и процедура PARSEC_ENQUEUE созданы от SYSDBA и выполняются с его
правами, поэтому прав на INSERT в PARSEC_QUEUE и на генератор службе не нужно.

Подробное описание скриптов, статусов и диагностики — в файле db\README.md.


================================================================================
 2. УСТАНОВКА СЛУЖБЫ ЧЕРЕЗ sc.exe
================================================================================

Имя службы по умолчанию: ParsecIntegrationService.
Файл службы после установки Inno Setup:
    C:\Program Files\ParsecIntegrationService\ParsecIntegrationClient.exe

Пример создания службы:

    sc create ParsecIntegrationService binPath= "C:\Program Files\ParsecIntegrationService\ParsecIntegrationClient.exe" start= auto DisplayName= "Parsec Integration Client"

    sc description ParsecIntegrationService "Модуль интеграции СКУД Артонит и СКУД Парсек"

    sc failure ParsecIntegrationService reset= 86400 actions= restart/60000/restart/60000/restart/60000

    sc config ParsecIntegrationService obj= "NT AUTHORITY\LocalService"

    sc start ParsecIntegrationService

Проверка:

    sc query ParsecIntegrationService

Правила синтаксиса sc.exe:

  * после знака "=" обязателен пробел: "start= auto", "binPath= ...",
    "DisplayName= ...". Без пробела команда завершится ошибкой;
  * binPath и DisplayName с пробелами заключайте в двойные кавычки;
  * sc create регистрирует службу, но не запускает её — нужен отдельный sc start;
  * учётной записи службы (по умолчанию NT AUTHORITY\LocalService) нужны права
    на запись в каталог Paths:MainPath из appsettings.json;
  * appsettings.json должен лежать рядом с exe. Установщик не перезаписывает его
    при обновлении.

Вместо sc.exe можно использовать готовый скрипт из папки поставки:

    .\install-service.ps1

Он делает то же самое: создаёт или обновляет службу, задаёт описание, перезапуск
при падении, учётную запись и запускает её.


================================================================================
 3. УДАЛЕНИЕ СЛУЖБЫ ЧЕРЕЗ sc.exe
================================================================================

    sc stop ParsecIntegrationService

    sc delete ParsecIntegrationService

Проверка, что службы больше нет:

    sc query ParsecIntegrationService

Примечания:

  * sc delete принимает имя службы (ParsecIntegrationService), а не DisplayName;
  * если служба ещё работает, sc delete помечает её на удаление, и она исчезнет
    только после остановки. Поэтому сначала stop, затем delete;
  * удаление службы не удаляет ни файлы программы, ни каталог данных
    (Paths:MainPath), ни журналы. Каталог C:\Program Files\ParsecIntegrationService
    удаляется установщиком или вручную.

Альтернатива — скрипт из папки поставки:

    .\uninstall-service.ps1


================================================================================
 4. ПЕРЕИМЕНОВАНИЕ СЛУЖБЫ ЧЕРЕЗ sc.exe
================================================================================

У sc.exe НЕТ команды rename. Переименование выполняется одним из двух способов.

4.1. Изменить только отображаемое имя (DisplayName)

Видимое имя в services.msc и в списке служб меняется, а системное имя службы
(ключ реестра) остаётся прежним — ParsecIntegrationService. Все команды sc
по-прежнему используют старое имя.

    sc config ParsecIntegrationService DisplayName= "Parsec Integration Client (новое имя)"

Проверка:

    sc query ParsecIntegrationService

Для этого способа останавливать службу не требуется.

4.2. Изменить системное имя службы

sc.exe не умеет переименовывать ключ службы. Правильный путь — создать службу
с новым именем, указав тот же exe, и удалить старую. Старую службу обязательно
остановите: с версии 4.1.4 второй экземпляр программы не запускается.

    sc stop ParsecIntegrationService

    sc create ParsecIntegrationService2 binPath= "C:\Program Files\ParsecIntegrationService\ParsecIntegrationClient.exe" start= auto DisplayName= "Parsec Integration Client"

    sc description ParsecIntegrationService2 "Модуль интеграции СКУД Артонит и СКУД Парсек"

    sc failure ParsecIntegrationService2 reset= 86400 actions= restart/60000/restart/60000/restart/60000

    sc config ParsecIntegrationService2 obj= "NT AUTHORITY\LocalService"

    sc delete ParsecIntegrationService

    sc start ParsecIntegrationService2

Проверка:

    sc query ParsecIntegrationService2

Примечания:

  * новая служба использует тот же exe и тот же appsettings.json, поэтому
    Paths:MainPath, строка подключения к базе и настройки Парсека остаются
    прежними;
  * не запускайте старую и новую службу одновременно: они читают одну очередь
    PARSEC_QUEUE. В версии 4.1.4 и новее второй экземпляр завершится сам, но
    лучше остановить старую службу до создания новой;
  * если служба работает под другой учётной записью, повторите для новой службы
    команду sc config ... obj= ... (и password= ..., если он нужен);
  * ручное переименование ветки реестра службы не поддерживается и может
    привести к неработоспособной службе — используйте создание и удаление.
