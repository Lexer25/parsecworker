/* ============================================================================
   Business-table triggers -> PARSEC_QUEUE.

   The old CARDINDEV triggers are intentionally left untouched: they keep the
   legacy queue working for rollback. These triggers are additional and only
   fill the new queue.

   Position 100: run after the vendor triggers (positions 0..2), so NEW values
   are final.

   CARD changes: 9 = add identifier, 10 = delete identifier, 11 = change
   identifier properties (period, ACTIVE). See db/README.md for the matrix.

   RE-RUN / UPDATE:
   Firebird 1.5 has neither CREATE OR ALTER TRIGGER nor DROP TRIGGER IF EXISTS,
   so every trigger is recreated as "drop trigger" + "create trigger". Each drop
   sits immediately before its create, so the window without the trigger is as
   short as possible. On the first run the drop of a missing trigger fails with
   "trigger not found": isql prints the error and continues with the next
   statement (AUTODDL is on by default, every DDL statement is committed at
   once). Do not run this file with isql -b. Stop the service before running:
   between drop and create an event fired in that window is lost.
   ============================================================================ */

set term ^ ;

/* ---------------------------------------------------------------- PEOPLE -- */

drop trigger parsec_q_people_ai^

create trigger parsec_q_people_ai for people active after insert position 100 as
begin
  execute procedure parsec_enqueue(
    3, new.id_pep, cast(null as varchar(50)),
    'PERSON:3:' || cast(new.id_pep as varchar(20)), 'PEOPLE');
end^

drop trigger parsec_q_people_au^

create trigger parsec_q_people_au for people active after update position 100 as
begin
  if (coalesce(new.id_org, -1) <> coalesce(old.id_org, -1)
   or coalesce(new.surname, '') <> coalesce(old.surname, '')
   or coalesce(new.name, '') <> coalesce(old.name, '')
   or coalesce(new.patronymic, '') <> coalesce(old.patronymic, '')
   or coalesce(new.tabnum, '') <> coalesce(old.tabnum, '')) then
  begin
    if (new.guid is not null and new.guid <> '') then
    begin
      execute procedure parsec_enqueue(
        35, new.id_pep, cast(null as varchar(50)),
        'PERSON:35:' || cast(new.id_pep as varchar(20)), 'PEOPLE');
    end
  end
end^

drop trigger parsec_q_people_ad^

create trigger parsec_q_people_ad for people active after delete position 100 as
begin
  update parsec_queue
     set status = 6,
         finished_at = current_timestamp,
         updated_at = current_timestamp,
         error_text = 'Отменено: человек удален'
   where id_pep = old.id_pep
     and operation in (3, 35)
     and status in (0, 3);

  if (old.guid is not null and old.guid <> '') then
  begin
    execute procedure parsec_enqueue(
      4, old.id_pep, cast(old.guid as varchar(50)),
      'PERSON:4:' || old.guid, 'PEOPLE');
  end
end^

/* ---------------------------------------------------------- ORGANIZATION -- */

drop trigger parsec_q_org_ai^

create trigger parsec_q_org_ai for organization active after insert position 100 as
begin
  if (new.guid is not null and new.guid <> '') then
  begin
    execute procedure parsec_enqueue(
      5, cast(null as integer), cast(new.guid as varchar(50)),
      'ORG:5:' || new.guid, 'ORGANIZATION');
  end
end^

drop trigger parsec_q_org_au^

create trigger parsec_q_org_au for organization active after update position 100 as
begin
  if (coalesce(new.id_parent, -1) <> coalesce(old.id_parent, -1)
   or coalesce(new.name, '') <> coalesce(old.name, '')
   or coalesce(new.divcode, '') <> coalesce(old.divcode, '')) then
  begin
    if (new.guid is not null and new.guid <> '') then
    begin
      execute procedure parsec_enqueue(
        55, cast(null as integer), cast(new.guid as varchar(50)),
        'ORG:55:' || new.guid, 'ORGANIZATION');
    end
  end
end^

drop trigger parsec_q_org_ad^

create trigger parsec_q_org_ad for organization active after delete position 100 as
begin
  update parsec_queue
     set status = 6,
         finished_at = current_timestamp,
         updated_at = current_timestamp,
         error_text = 'Отменено: организация удалена'
   where id_card = old.guid
     and operation in (5, 55)
     and status in (0, 3);

  if (old.guid is not null and old.guid <> '') then
  begin
    execute procedure parsec_enqueue(
      6, cast(null as integer), cast(old.guid as varchar(50)),
      'ORG:6:' || old.guid, 'ORGANIZATION');
  end
end^

/* ------------------------------------------------------------------ CARD -- */

drop trigger parsec_q_card_ai^

create trigger parsec_q_card_ai for card active after insert position 100 as
declare variable v_accessname integer;
begin
  if (new.id_cardtype = 1) then
  begin
    if (new.id_pep is not null and new.id_card is not null) then
    begin
      execute procedure parsec_enqueue(
        9, new.id_pep, cast(new.id_card as varchar(50)),
        'CARD:9:' || new.id_card, 'CARD');
    end

    for select ssu.id_accessname
          from ss_accessuser ssu
         where ssu.id_pep = new.id_pep
           and ssu.id_accessname in (
             select distinct a.id_accessname
               from access a
               join accessname an on an.id_accessname = a.id_accessname
               join device d2 on d2.id_dev = a.id_dev
               join device d on d.id_ctrl = d2.id_ctrl
               join server s on s.id_server = d.id_server
               join servertypelist stl on stl.id_server = s.id_server
               join servertype st on st.id = stl.id_type
              where d2.id_reader is not null
                and st.sname = 'parsec'
                and a.id_accessname > 1)
        into :v_accessname
    do
      execute procedure parsec_enqueue(
        7, new.id_pep, cast(:v_accessname as varchar(50)),
        'ACC:7:' || cast(new.id_pep as varchar(20)) || ':' || cast(:v_accessname as varchar(20)),
        'SS_ACCESSUSER');
  end
end^

/* Изменение карты. Операции:
   10        — ID_CARDTYPE перестал быть 1: идентификатор в Парсеке не нужен;
   9         — ID_CARDTYPE стал 1: идентификатор нужно добавить;
   10 и 9    — сменился владелец: старому удалить, новому добавить;
   11        — изменились TIMESTART/TIMEEND/ACTIVE: изменить свойства идентификатора.
   Пока идёт 9 или 11, событие склеивается по DEDUP_KEY, поэтому на одну карту в
   очереди всегда не больше одного незавершённого события каждого вида. */
drop trigger parsec_q_card_au^

create trigger parsec_q_card_au for card active after update position 100 as
declare variable v_old_type integer;
declare variable v_new_type integer;
begin
  v_old_type = coalesce(old.id_cardtype, 0);
  v_new_type = coalesce(new.id_cardtype, 0);

  if (v_old_type = 1 and v_new_type <> 1) then
  begin
    if (old.id_card is not null) then
    begin
      update parsec_queue
         set status = 6,
             finished_at = current_timestamp,
             updated_at = current_timestamp,
             error_text = 'Отменено: карта больше не карта доступа (ID_CARDTYPE <> 1)'
       where id_card = old.id_card
         and operation in (9, 11)
         and status in (0, 3);

      execute procedure parsec_enqueue(
        10, old.id_pep, cast(old.id_card as varchar(50)),
        'CARD:10:' || old.id_card, 'CARD');
    end
  end
  else
  if (v_old_type <> 1 and v_new_type = 1) then
  begin
    if (new.id_pep is not null and new.id_card is not null) then
    begin
      execute procedure parsec_enqueue(
        9, new.id_pep, cast(new.id_card as varchar(50)),
        'CARD:9:' || new.id_card, 'CARD');
    end
  end
  else
  if (v_new_type = 1) then
  begin
    if (coalesce(new.id_pep, -1) <> coalesce(old.id_pep, -1)) then
    begin
      /* Смена владельца: сначала удалить у старого, затем добавить новому.
         Незавершённые 9 и 11 по этой карте отменяются, чтобы они не применились
         к старому владельцу: 10 получит меньший ID и уйдёт первым. */
      if (old.id_card is not null) then
      begin
        update parsec_queue
           set status = 6,
               finished_at = current_timestamp,
               updated_at = current_timestamp,
               error_text = 'Отменено: карта перепривязана'
         where id_card = old.id_card
           and operation in (9, 11)
           and status in (0, 3);
      end

      if (old.id_pep is not null and old.id_card is not null) then
      begin
        execute procedure parsec_enqueue(
          10, old.id_pep, cast(old.id_card as varchar(50)),
          'CARD:10:' || old.id_card, 'CARD');
      end

      if (new.id_pep is not null and new.id_card is not null) then
      begin
        execute procedure parsec_enqueue(
          9, new.id_pep, cast(new.id_card as varchar(50)),
          'CARD:9:' || new.id_card, 'CARD');
      end
    end
    else
    if (coalesce(new.timestart, cast('1900-01-01' as timestamp))
          <> coalesce(old.timestart, cast('1900-01-01' as timestamp))
     or coalesce(new.timeend, cast('1900-01-01' as timestamp))
          <> coalesce(old.timeend, cast('1900-01-01' as timestamp))
     or coalesce(new."ACTIVE", -1) <> coalesce(old."ACTIVE", -1)) then
    begin
      if (new.id_pep is not null and new.id_card is not null) then
      begin
        execute procedure parsec_enqueue(
          11, new.id_pep, cast(new.id_card as varchar(50)),
          'CARD:11:' || new.id_card, 'CARD');
      end
    end
  end
end^

drop trigger parsec_q_card_ad^

create trigger parsec_q_card_ad for card active after delete position 100 as
begin
  if (old.id_cardtype = 1) then
  begin
    update parsec_queue
       set status = 6,
           finished_at = current_timestamp,
           updated_at = current_timestamp,
           error_text = 'Отменено: карта удалена'
     where id_card = old.id_card
       and operation in (9, 11)
       and status in (0, 3);

    if (old.id_pep is not null and old.id_card is not null) then
    begin
      if (exists (select p.id_pep from people p where p.id_pep = old.id_pep)) then
      begin
        execute procedure parsec_enqueue(
          10, old.id_pep, cast(old.id_card as varchar(50)),
          'CARD:10:' || old.id_card, 'CARD');
      end
    end
  end
end^

/* -------------------------------------------------- SS_ACCESSUSER (7/8) --- */

drop trigger parsec_q_ssaccess_ai^

create trigger parsec_q_ssaccess_ai for ss_accessuser active after insert position 100 as
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
         where an.id_accessname = new.id_accessname
           and st.sname = 'parsec')) then
  begin
    if (exists (
          select c.id_card
            from card c
           where c.id_pep = new.id_pep
             and c.id_cardtype = 1
             and c."ACTIVE" > 0)) then
    begin
      execute procedure parsec_enqueue(
        7, new.id_pep, cast(new.id_accessname as varchar(50)),
        'ACC:7:' || cast(new.id_pep as varchar(20)) || ':' || cast(new.id_accessname as varchar(20)),
        'SS_ACCESSUSER');
    end
  end
end^

/* Удаление SS_ACCESSUSER: сначала отменяем ещё не обработанную выдачу этой же
   категории (7), затем ставим снятие (8). Проверка PEOPLE обязательна: при
   удалении человека FK-каскад удаляет SS_ACCESSUSER уже после того, как строка
   PEOPLE удалена. Без проверки триггер ставил 8 по всем parsec-категориям
   удалённого человека; служба 4.1.3+ такие события пропускает (статус 5), а
   более старые падали с "Value cannot be null. (Parameter 'g')" и
   останавливали очередь. */
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
