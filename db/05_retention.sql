/* ============================================================================
   Retention: remove completed events older than 12 months.
   The log is removed by ON DELETE CASCADE.
   Run monthly (scheduled task / DBA job), not by the service.
   ============================================================================ */

delete from parsec_queue
 where status in (2, 4, 5, 6)
   and coalesce(finished_at, updated_at, created_at) < current_timestamp - 365;

commit;
