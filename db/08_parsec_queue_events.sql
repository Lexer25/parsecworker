/* ============================================================================
   PARSEC_QUEUE -> Firebird events.

   The queue viewer (ParsecQueueViewer.Win32) reloads the queue when the
   database says it has changed: the trigger below posts the event
   PARSEC_QUEUE_CHANGED on every insert, update and delete of PARSEC_QUEUE.
   The client listens with FbRemoteEvent and does not need to poll.

   Firebird delivers an event to clients when the transaction that posted it
   commits, so a viewer refresh always sees the committed state. Several
   changes in one transaction produce one event.

   Firebird 1.5 has neither CREATE OR ALTER TRIGGER nor DROP TRIGGER IF
   EXISTS, so the trigger is recreated as "drop trigger" + "create trigger".
   On the first run the drop fails with "trigger not found": isql prints the
   error and continues with the next statement (AUTODDL is on by default,
   every DDL statement is committed at once). Do not run this file with
   isql -b. Stop the service before running: between drop and create an event
   fired in that window is lost.

   Run after 01..03 (and after 06/07 if they were applied):
     isql -user SYSDBA -password <pwd> localhost:D:\path\to\database.gdb -i db\08_parsec_queue_events.sql

   The event name must match QueueEventWatcher.EventName in
   ParsecQueueViewer.Win32\QueueEventWatcher.cs.
   ============================================================================ */

set term ^ ;

drop trigger parsec_queue_events^

create trigger parsec_queue_events for parsec_queue active after insert or update or delete position 0 as
begin
  post_event 'PARSEC_QUEUE_CHANGED';
end^

set term ; ^

commit;
