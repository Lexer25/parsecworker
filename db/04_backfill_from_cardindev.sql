/* ============================================================================
   One-time backfill of still-pending integration operations from CARDINDEV.
   Only operations 3..10, 35, 55 are migrated; 1/2 (device card index) are not
   part of the Parsec integration and are ignored.
   Rows in CARDINDEV are not deleted.
   ============================================================================ */

insert into parsec_queue
  (operation, id_pep, id_card, status, attempts, max_attempts,
   dedup_key, source_table, created_at, next_attempt)
select cd.operation,
       cd.id_pep,
       cd.id_card,
       0,
       cd.attempts,
       20,
       'BACKFILL:' || cast(cd.id_cardindev as varchar(20)),
       'CARDINDEV',
       current_timestamp,
       current_timestamp
  from cardindev cd
 where cd.operation in (3, 4, 5, 6, 7, 8, 9, 10, 35, 55)
   and cd.attempts < 20
   and not exists (
         select 1
           from parsec_queue q
          where q.dedup_key = 'BACKFILL:' || cast(cd.id_cardindev as varchar(20)));

commit;
