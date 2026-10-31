/* ---------------------------------------------------------------------------
   PARSEC_Q_SSACCESS_AD — снятие категории доступа с сотрудника (операция 8).

   Событие ставится, когда у человека удаляется запись SS_ACCESSUSER, а
   категория при этом обслуживается Парсеком (цепочка accessname -> access ->
   device -> контроллер -> server -> servertype.sname = 'parsec').

   Что делает триггер:
     1) отменяет ещё не обработанную выдачу этой же категории (операция 7),
        иначе пара "выдали -> сняли" сгоняет Парсек туда-обратно;
     2) ставит операцию 8, если человек ещё существует и у него есть
        действующий идентификатор (карта типа 1 с ACTIVE > 0).

   Отличие от версии из db/03_parsec_queue_triggers.sql: добавлена проверка
   PEOPLE (раньше её не было). При удалении
   человека FK-каскад удаляет SS_ACCESSUSER уже после того, как строка PEOPLE
   удалена, поэтому без проверки триггер ставил операцию 8 по всем
   parsec-категориям удалённого человека. Служба 4.1.3+ такие события
   пропускает (статус 5), но до 4.1.3 они падали с
   "Value cannot be null. (Parameter 'g')" и останавливали очередь.

   Firebird 1.5 не поддерживает CREATE OR ALTER TRIGGER, поэтому drop + create.
   Скрипт можно запускать повторно: отсутствующий триггер даст безобидную
   ошибку "trigger not found", create выполнится.
   --------------------------------------------------------------------------- */

set term ^;

drop trigger parsec_q_ssaccess_ad^

create trigger parsec_q_ssaccess_ad for ss_accessuser active after delete position 100 as
begin
  if (exists (
        select 1
          from accessname an
          join access a on a.id_accessname = an.id_accessname
          join device d on d.id_dev = a.id_dev
          join device d2 on d2.id_ctrl = d.id_ctrl and d2.id_reader is null
          join server s on s.id_server = d2.id_server
          join servertypelist stl on stl.id_server = s.id_server
          join servertype st on st.id = stl.id_type
         where an.id_accessname = old.id_accessname
           and st.sname = 'parsec')) then
  begin
    update parsec_queue
       set status = 6,
           finished_at = current_timestamp,
           updated_at = current_timestamp,
           error_text = 'Отменено: категория доступа снята'
     where id_pep = old.id_pep
       and id_card = cast(old.id_accessname as varchar(50))
       and operation = 7
       and status in (0, 3);

    if (exists (select 1 from people p where p.id_pep = old.id_pep)) then
    begin
      if (exists (
            select c.id_card
              from card c
             where c.id_pep = old.id_pep
               and c.id_cardtype = 1
               and c."ACTIVE" > 0)) then
      begin
        execute procedure parsec_enqueue(
          8, old.id_pep, cast(old.id_accessname as varchar(50)),
          'ACC:8:' || cast(old.id_pep as varchar(20)) || ':' || cast(old.id_accessname as varchar(20)),
          'SS_ACCESSUSER');
      end
    end
  end
end^

set term ; ^

commit;
