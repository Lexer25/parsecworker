# Очередь интегратора PARSEC_QUEUE

С версии 4.0.0 служба читает задания не из `CARDINDEV`, а из собственной таблицы
`PARSEC_QUEUE`. Триггеры на бизнес-таблицах пишут туда событие, служба меняет его
статус и никогда не удаляет строку. Так формируется история работы интегратора.

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
| 5 | пропущено (неизвестная операция, `SkipErrCode`) | служба |
| 6 | отменено (объект удалён/перепривязан) | триггеры |

Обработка идёт строго по `ID`. **Первая же ошибка останавливает проход**: следующие
события не трогаются. Служба выдерживает `Service:ErrorTimeoutMinutes` (по умолчанию
1 минута) и повторяет ту же строку. После `MAX_ATTEMPTS` (по умолчанию 20) строка
получает статус 4 и перестаёт блокировать очередь; дальше идут следующие события.

Если у операции удаления (4, 6) `ID_CARD` не является GUID (тестовые данные вида
`TEST-ORG-...`), событие сразу пропускается (статус 5): объекта с таким
идентификатором в Парсеке быть не может. Для операций создания/обновления (5, 55)
некорректный GUID — ошибка.

## Склейка событий

`PARSEC_ENQUEUE` ищет незавершённое событие с тем же `DEDUP_KEY` (статус 0 или 3).
Если нашло — обновляет его и сбрасывает `ATTEMPTS`. Это защищает от лавины при
массовых правках: десять изменений одного человека подряд дадут одно событие 35.

Событие со статусом 1 (в работе) не трогается: новое состояние встанет отдельной
строкой и будет обработано после текущей.

`DEDUP_KEY` имеет вид `PERSON:35:<id_pep>`, `ORG:5:<guid>`, `CARD:9:<id_card>`,
`ACC:7:<id_pep>:<id_accessname>`.

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
| `PARSEC_Q_CARD_AU` | смена владельца/срока действия | 10 + 9 | old/new.id_pep | old/new.id_card |
| `PARSEC_Q_CARD_AD` | delete CARD | 10 | old.id_pep | old.id_card |
| `PARSEC_Q_SSACCESS_AI` | insert SS_ACCESSUSER | 7 | new.id_pep | new.id_accessname |
| `PARSEC_Q_SSACCESS_AD` | delete SS_ACCESSUSER | 8 | old.id_pep | old.id_accessname |

Триггеры стоят в `POSITION 100`, то есть срабатывают после вендорских триггеров
(0..2) и видят итоговые значения полей.

При удалении/перепривязке объекта незавершённые события не удаляются, а получают
статус 6 с пояснением в `ERROR_TEXT`. История остаётся полной.

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
