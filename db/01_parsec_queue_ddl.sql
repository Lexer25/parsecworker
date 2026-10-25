/* ============================================================================
   Parsec integration queue + history.
   Target: Firebird 1.5 (ODS 10.1), dialect 3, WIN1251.
   Run once under SYSDBA. Script is not idempotent: for a re-run use
   99_drop_parsec_queue.sql first.
   ============================================================================ */

set term ^ ;

create generator gen_parsec_queue_id^
create generator gen_parsec_queue_log_id^

create table parsec_queue (
  id            bigint       not null,
  operation     integer      not null,
  id_pep        integer,
  id_card       varchar(50),
  status        integer      not null,
  attempts      integer      not null,
  max_attempts  integer      not null,
  next_attempt  timestamp,
  lease_until   timestamp,
  created_at    timestamp,
  updated_at    timestamp,
  started_at    timestamp,
  finished_at   timestamp,
  error_code    integer,
  error_text    varchar(1000),
  duration_ms   integer,
  worker_id     varchar(64),
  source_table  varchar(63),
  dedup_key     varchar(128),
  constraint pk_parsec_queue primary key (id),
  constraint ck_parsec_queue_status check (status in (0, 1, 2, 3, 4, 5, 6))
)^

create index ix_parsec_queue_poll    on parsec_queue (status, id)^
create index ix_parsec_queue_created on parsec_queue (created_at)^
create index ix_parsec_queue_dedup   on parsec_queue (dedup_key)^
create index ix_parsec_queue_pep     on parsec_queue (id_pep)^
create index ix_parsec_queue_card    on parsec_queue (id_card)^

create table parsec_queue_log (
  id          bigint       not null,
  queue_id    bigint       not null,
  attempt_no  integer      not null,
  started_at  timestamp,
  finished_at timestamp,
  status      integer,
  error_code  integer,
  error_text  varchar(1000),
  duration_ms integer,
  worker_id   varchar(64),
  details     blob sub_type text,
  constraint pk_parsec_queue_log primary key (id)
)^

create index ix_parsec_queue_log_q on parsec_queue_log (queue_id, attempt_no)^

alter table parsec_queue_log
  add constraint fk_parsec_queue_log_q
  foreign key (queue_id) references parsec_queue (id) on delete cascade^

create trigger parsec_queue_bi for parsec_queue active before insert position 0 as
begin
  if (new.id is null) then new.id = gen_id(gen_parsec_queue_id, 1);
  if (new.status is null) then new.status = 0;
  if (new.attempts is null) then new.attempts = 0;
  if (new.max_attempts is null) then new.max_attempts = 20;
  if (new.created_at is null) then new.created_at = current_timestamp;
  if (new.next_attempt is null) then new.next_attempt = current_timestamp;
end^

create trigger parsec_queue_log_bi for parsec_queue_log active before insert position 0 as
begin
  if (new.id is null) then new.id = gen_id(gen_parsec_queue_log_id, 1);
  if (new.finished_at is null) then new.finished_at = current_timestamp;
end^

set term ; ^

commit;
