/* ============================================================================
   PARSEC_ENQUEUE: one entry point for all triggers.

   Coalescing: if an event with the same dedup_key is still pending (status 0)
   or waiting for retry (status 3), it is updated in place, attempts are reset.
   Events that are already in progress (status 1) are not touched: a new row is
   inserted, so the worker will process the newer state after the current one.
   ============================================================================ */

set term ^ ;

create procedure parsec_enqueue (
  p_operation    integer,
  p_id_pep       integer,
  p_id_card      varchar(50),
  p_dedup_key    varchar(128),
  p_source_table varchar(63)
) as
declare variable v_id bigint;
begin
  v_id = null;

  select max(q.id)
    from parsec_queue q
   where q.dedup_key = :p_dedup_key
     and q.status in (0, 3)
    into :v_id;

  if (v_id is not null) then
  begin
    update parsec_queue q
       set q.operation    = :p_operation,
           q.id_pep       = :p_id_pep,
           q.id_card      = :p_id_card,
           q.status       = 0,
           q.attempts     = 0,
           q.next_attempt = current_timestamp,
           q.started_at   = null,
           q.finished_at  = null,
           q.lease_until  = null,
           q.error_code   = null,
           q.error_text   = null,
           q.duration_ms  = null,
           q.updated_at   = current_timestamp
     where q.id = :v_id;
  end
  else
  begin
    insert into parsec_queue
      (operation, id_pep, id_card, status, attempts, max_attempts,
       dedup_key, source_table, created_at, next_attempt)
    values
      (:p_operation, :p_id_pep, :p_id_card, 0, 0, 20,
       :p_dedup_key, :p_source_table, current_timestamp, current_timestamp);
  end
end^

set term ; ^

commit;
