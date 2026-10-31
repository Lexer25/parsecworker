# Очередь интегратора PARSEC_QUEUE

С версии 4.0.0 служба читает задания не из `CARDINDEV`, а из собственной таблицы
`PARSEC_QUEUE`. Триггеры на бизнес-таблицах пишут туда событие, служба меняет его
статус и никогда не удаляет строку. Так формируется история работы интегратора.

С версии 4.1.0 добавлена операция 11 — изменение свойств идентификатора (срок действия,
тип карты, `ACTIVE`). Триггеры `CARD` пересоздаются скриптом `06` (см. «Обновление
4.0.0 → 4.1.0»).

Старые триггеры `CARDINDEV` намеренно не изменяются: они остаются для отката и для
других потребителей. Новые триггеры работают параллельно и пишут только в
`PARSEC_QUEUE`.

## Файлы

| Файл | Что делает |
|---|---|
| `01_parsec_queue_ddl.sql` | генераторы, таблицы `PARSEC_QUEUE` и `PARSEC_QUEUE_LOG`, индексы, FK, before-insert триггеры |
| `02_parsec_enqueue.sql` | процедура `PARSEC_ENQUEUE` — единая точка постановки события, склеивает повторные события по `DEDUP_KEY` |
| `03_parsec_queue_triggers.sql` | триггеры на `PEOPLE`, `ORGANIZATION`, `CARD`, `SS_ACCESSUSER` |
| `04_backfill_from_cardindev.sql` | разовый перенос незавершённых операций 3..10, 35, 55 из `CARDINDEV` |
| `05_retention.sql` | удаление завершённых событий старше 12 месяцев |
| `06_parsec_queue_card_11.sql` | обновление 4.0.0 → 4.1.0: пересоздание триггеров `CARD` (операция 11, смена `ID_CARDTYPE`) |
| `99_drop_parsec_queue.sql` | удалить всё созданное (уничтожает историю; только для тестовой базы) |

Скрипты рассчитаны на **Firebird 1.5 (ODS 10.1), dialect 3, WIN1251**. Для более новых
версий Firebird они тоже работают: `DATEADD`, `EXECUTE BLOCK` и identity-колонки
сознательно не используются.

## Порядок применения

Выполнять под `SYSDBA` через `isql`:

```bat
isql -user SYSDBA -password <пароль> localhost:D:\path\to\database.gdb -i db\01_parsec_queue_ddl.sql
isql -user SYSDBA -password <пароль> localhost:D:\path\to\database.gdb -i db\02_parsec_enqueue.sql
isql -user SYSDBA -password <пароль> localhost:D:\path\to\database.gdb -i db\03_parsec_queue_triggers.sql
```

Проверка:

```sql
select count(*) from parsec_queue;
select rdb$trigger_name from rdb$triggers where rdb$trigger_name starting with 'PARSEC_Q_';
```

`04_backfill` нужен только если в `CARDINDEV` остались необработанные операции 3..10,
35, 55. Операции 1/2 (индекс карт на устройствах) к Парсеку не относятся и не переносятся.
Запускайте его **до** включения новых триггеров: после включения те же бизнес-события
уже лежат в `PARSEC_QUEUE`, и backfill создаст дубликаты.

### Обновление 4.0.0 → 4.1.0

На базе, где уже выполнены `01..03` версии 4.0.0, дополнительно выполнить один скрипт:

```bat
isql -user SYSDBA -password <пароль> localhost:D:\path\to\database.gdb -i db\06_parsec_queue_card_11.sql
```

Он пересоздаёт триггеры `PARSEC_Q_CARD_AU` и `PARSEC_Q_CARD_AD`: в 4.0.0 они ставили
только операцию 9 и не видели ни `ACTIVE`, ни смену `ID_CARDTYPE`. Пока скрипт не
выполнен, служба 4.1.0 работает, но события операции 11 в очередь не попадают.

Firebird 1.5 не поддерживает `CREATE OR ALTER TRIGGER`, поэтому скрипт делает
`drop trigger` + `create trigger`. `isql` фиксирует каждый DDL-оператор сразу
(`AUTODDL` включён по умолчанию) и продолжает работу после ошибки отдельного оператора.
Поэтому:

- оборванный на середине скрипт можно запустить повторно: отсутствующий триггер даст
  безобидную ошибку «trigger not found», оба триггера будут созданы заново;
- после запуска проверьте, что в выводе нет `Statement failed` и что оба триггера есть:

```sql
select rdb$trigger_name from rdb$triggers
 where rdb$trigger_name in ('PARSEC_Q_CARD_AU', 'PARSEC_Q_CARD_AD');
```

Перед обновлением остановите службу, чтобы события не терялись в момент пересоздания
триггеров.

### Кодировка скриптов

Файлы скриптов — UTF-8. `isql` версии 1.5 не умеет задавать клиентскую кодировку
(ключа `-ch` нет), а база — `WIN1251`, поэтому русские тексты в `ERROR_TEXT`
(сообщения об отменённых событиях) сохранятся как UTF-8-байты. Если это важно,
перекодируйте файл в `WIN1251` перед запуском или применяйте скрипт клиентом с явно
заданной кодировкой (IBExpert, FlameRobin). На работу интегратора это не влияет:
`ERROR_TEXT` заполняют только триггеры, служба его не читает.

```powershell
# перекодировать UTF-8 -> WIN1251 перед запуском isql 1.5
$t = [IO.File]::ReadAllText('db\06_parsec_queue_card_11.sql', [Text.Encoding]::UTF8)
[IO.File]::WriteAllText("$env:TEMP\06_win1251.sql", $t, [Text.Encoding]::GetEncoding(1251))
```

Если служба работает не под `SYSDBA`, выдать права:

```sql
grant select, update on parsec_queue to <service_user>;
grant select, insert on parsec_queue_log to <service_user>;
```

Триггеры и `PARSEC_ENQUEUE` созданы владельцем-`SYSDBA` и выполняются с его правами;
службе не нужно право на `INSERT` в `PARSEC_QUEUE` или на генератор.

## Статусы

| Код | Значение | Кто ставит |
|---|---|---|
| 0 | новая | триггер/`PARSEC_ENQUEUE` |
| 1 | в работе | служба, `TryClaimQueueRow` |
| 2 | успешно | служба |
| 3 | ошибка, будет повтор | служба |
| 4 | ошибка, попытки исчерпаны (dead letter) | служба |
| 5 | пропущено (неизвестная операция, `SkipErrCode`, нет данных для операций 7/8) | служба |
| 6 | отменено (объект удалён/перепривязан) | триггеры |

Обработка идёт строго по `ID`. **Первая же ошибка останавливает проход**: следующие
события не трогаются. Служба выдерживает `Service:ErrorTimeoutMinutes` (по умолчанию
1 минута) и повторяет ту же строку. После `MAX_ATTEMPTS` (по умолчанию 20) строка
получает статус 4 и перестаёт блокировать очередь; дальше идут следующие события.

Если у операции удаления (4, 6) `ID_CARD` не является GUID (тестовые данные вида
`TEST-ORG-...`), событие сразу пропускается (статус 5): объекта с таким
идентификатором в Парсеке быть не может. Для операций создания/обновления (5, 55)
некорректный GUID — ошибка.

Для операций 7 и 8 (категория доступа) служба сначала проверяет, что в базе есть
пара `ID_PEP` + `ID_ACCESSNAME` и у обоих заполнены GUID. Если пары нет (сотрудника
или категории доступа уже не существует) — событие пропускается (статус 5), причина
пишется в `ERROR_TEXT`, проход очереди при этом не останавливается. До 4.1.3 такой
случай завершался ошибкой `Value cannot be null. (Parameter 'g')` и блокировал очередь.

Операции 9, 10, 11 требуют числовой `ID_CARD` (номер карты) и `ID_PEP`: иначе событие
получает ошибку (коды 30, 32, 34) и не обрабатывается.

## Изменение свойств идентификатора (операция 11)

С 4.1.0 триггер Card пишет операцию 11, когда у карты типа 1 меняется срок действия
(`TIMESTART`/`TIMEEND`) или `ACTIVE`. Служба приводит идентификатор в Парсеке к
состоянию базы Артонит:

| Что в базе | Что делает служба |
|---|---|
| `ID_CARDTYPE` ≠ 1 | `DeleteIdentifier` — идентификатор в Парсеке не нужен |
| идентификатора нет | `AddPersonIdentifier` (`IdentifierTemp` или идентификатор без дат) |
| идентификатор есть | `ChangePersonIdentifier`: срок действия по таблице выбора, группа доступа, маска привилегий, тип и имя сохраняются |
| срок действия уже совпадает (допуск 1 с) | `Change` не вызывается |
| `ACTIVE` > 0 | `SetIdentifierExtraData`: `ENTRY_LIMIT` = -1, без ограничения проходов |
| `ACTIVE` = 0 | `SetIdentifierExtraData`: `ENTRY_LIMIT` = 0, доступ запрещён |
| `ACTIVE` не задан (NULL) | `ENTRY_LIMIT` не меняется, в журнал — предупреждение |
| `TIMESTART` > `TIMEEND` | ошибка (код 27): проход очереди останавливается, карту нужно исправить |

Таблица выбора срока действия (`VALID_FROM`/`VALID_TO`): обе даты — как есть;
`TIMESTART` = NULL — `VALID_FROM` = текущие сутки 00:00:00; `TIMEEND` = NULL —
`VALID_TO` = 31.12.9999 23:59:59; обе NULL — идентификатор без дат.
Тип объекта в SOAP определяется наличием `TIMEEND`, а не `TIMESTART`.

## Склейка событий

`PARSEC_ENQUEUE` ищет незавершённое событие с тем же `DEDUP_KEY` (статус 0 или 3).
Если нашло — обновляет его и сбрасывает `ATTEMPTS`. Это защищает от лавины при
массовых правках: десять изменений одного человека подряд дадут одно событие 35.

Событие со статусом 1 (в работе) не трогается: новое состояние встанет отдельной
строкой и будет обработано после текущей.

`DEDUP_KEY` имеет вид `PERSON:35:<id_pep>`, `ORG:5:<guid>`, `CARD:9:<id_card>`,
`CARD:11:<id_card>`, `ACC:7:<id_pep>:<id_accessname>`.

## Карта триггеров

| Триггер | Событие | operation | id_pep | id_card |
|---|---|---|---|---|
| `PARSEC_Q_PEOPLE_AI` | insert PEOPLE | 3 | new.id_pep | — |
| `PARSEC_Q_PEOPLE_AU` | update ФИО/табельный/организации | 35 | new.id_pep | — |
| `PARSEC_Q_PEOPLE_AD` | delete PEOPLE | 4 | old.id_pep | old.guid |
| `PARSEC_Q_ORG_AI` | insert ORGANIZATION | 5 | — | new.guid |
| `PARSEC_Q_ORG_AU` | update name/id_parent/divcode | 55 | — | new.guid |
| `PARSEC_Q_ORG_AD` | delete ORGANIZATION | 6 | — | old.guid |
| `PARSEC_Q_CARD_AI` | insert CARD (id_cardtype=1) | 9 + 7 | new.id_pep | new.id_card / id_accessname |
| `PARSEC_Q_CARD_AU` | `ID_CARDTYPE` 1 → другой | 10 | old.id_pep | old.id_card |
| `PARSEC_Q_CARD_AU` | `ID_CARDTYPE` другой → 1 | 9 | new.id_pep | new.id_card |
| `PARSEC_Q_CARD_AU` | смена владельца (карта типа 1) | 10 + 9 | old/new.id_pep | old/new.id_card |
| `PARSEC_Q_CARD_AU` | смена `TIMESTART`/`TIMEEND`/`ACTIVE` (карта типа 1) | 11 | new.id_pep | new.id_card |
| `PARSEC_Q_CARD_AD` | delete CARD | 10 | old.id_pep | old.id_card |
| `PARSEC_Q_SSACCESS_AI` | insert SS_ACCESSUSER | 7 | new.id_pep | new.id_accessname |
| `PARSEC_Q_SSACCESS_AD` | delete SS_ACCESSUSER | 8 | old.id_pep | old.id_accessname |

Триггеры стоят в `POSITION 100`, то есть срабатывают после вендорских триггеров
(0..2) и видят итоговые значения полей.

При удалении/перепривязке объекта незавершённые события не удаляются, а получают
статус 6 с пояснением в `ERROR_TEXT`. История остаётся полной. При смене владельца
незавершённые 9 и 11 по этой карте тоже отменяются (статус 6): они относились к
старому владельцу, а `10` и `9` получают возрастающие `ID` и уходят в правильном
порядке — сначала удаление, затем добавление.

## История и диагностика

`PARSEC_QUEUE` — одно событие и его текущее состояние. `PARSEC_QUEUE_LOG` — каждая
попытка: старт, финиш, статус, код/текст ошибки, длительность, исполнитель и JSON
`DETAILS` с тем, что обрабатывалось.

```sql
-- состояние очереди
select status, count(*) from parsec_queue group by status;

-- последние события
select id, operation, id_pep, id_card, status, attempts, created_at, finished_at, error_text
from parsec_queue order by id desc rows 50;

-- dead letter
select id, operation, id_pep, id_card, attempts, error_text, finished_at
from parsec_queue where status = 4 order by id desc;

-- зависшие в работе
select id, operation, worker_id, started_at, lease_until
from parsec_queue where status = 1
  and (lease_until is null or lease_until < current_timestamp);

-- история по человеку
select id, operation, status, attempts, created_at, finished_at, error_text
from parsec_queue where id_pep = 12345 order by id desc;

-- попытки по событию (id — глобальный порядок, attempt_no может повторяться после склейки)
select attempt_no, started_at, finished_at, status, error_code, error_text, duration_ms, details
from parsec_queue_log where queue_id = 42 order by id;
```

`DETAILS` намеренно не содержит полных SOAP-конвертов: только операцию, идентификаторы,
результат и код ошибки. ФИО и табельные номера в историю не пишутся.

## Очистка истории

`05_retention.sql` удаляет завершённые события старше 365 дней; записи
`PARSEC_QUEUE_LOG` удаляются каскадом. Запускать по расписанию (раз в месяц), не из
службы. Если история нужна навсегда — не запускать, но следить за размером таблицы.

## Откат

1. Остановить службу.
2. Вернуть предыдущую версию exe (она читает `CARDINDEV`; старые триггеры не тронуты).
3. При необходимости удалить новые объекты `99_drop_parsec_queue.sql`.

События, созданные после переключения, есть только в `PARSEC_QUEUE`; перед откатом
их нужно либо обработать новой службой, либо перенести обратно в `CARDINDEV`.
Одновременно старую и новую службу не запускать: они обработают одни и те же
бизнес-события дважды.
