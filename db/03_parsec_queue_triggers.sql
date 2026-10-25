/* ============================================================================
   Business-table triggers -> PARSEC_QUEUE.

   The old CARDINDEV triggers are intentionally left untouched: they keep the
   legacy queue working for rollback. These triggers are additional and only
   fill the new queue.

   Position 100: run after the vendor triggers (positions 0..2), so NEW values
   are final.
   ============================================================================ */

set term ^ ;

/* ---------------------------------------------------------------- PEOPLE -- */

create trigger parsec_q_people_ai for people active after insert position 100 as
begin
  execute procedure parsec_enqueue(
    3, new.id_pep, cast(null as varchar(50)),
    'PERSON:3:' || cast(new.id_pep as varchar(20)), 'PEOPLE');
end^

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

create trigger parsec_q_org_ai for organization active after insert position 100 as
begin
  if (new.guid is not null and new.guid <> '') then
  begin
    execute procedure parsec_enqueue(
      5, cast(null as integer), cast(new.guid as varchar(50)),
      'ORG:5:' || new.guid, 'ORGANIZATION');
  end
end^

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

create trigger parsec_q_card_au for card active after update position 100 as
begin
  if (new.id_cardtype = 1) then
  begin
    if (coalesce(new.id_pep, -1) <> coalesce(old.id_pep, -1)) then
    begin
      update parsec_queue
         set status = 6,
             finished_at = current_timestamp,
             updated_at = current_timestamp,
             error_text = 'Отменено: карта перепривязана'
       where id_card = old.id_card
         and operation = 9
         and status in (0, 3);

      if (old.id_pep is not null and old.id_card is not null) then
      begin
        execute procedure parsec_enqueue(
          10, old.id_pep, cast(old.id_card as varchar(50)),
          'CARD:10:' || old.id_card, 'CARD');
      end
    end

    if (coalesce(new.id_pep, -1) <> coalesce(old.id_pep, -1)
     or coalesce(new.timestart, cast('1900-01-01 00:00:00' as timestamp))
        <> coalesce(old.timestart, cast('1900-01-01 00:00:00' as timestamp))
     or coalesce(new.timeend, cast('1900-01-01 00:00:00' as timestamp))
        <> coalesce(old.timeend, cast('1900-01-01 00:00:00' as timestamp))) then
    begin
      if (new.id_pep is not null and new.id_card is not null) then
      begin
        execute procedure parsec_enqueue(
          9, new.id_pep, cast(new.id_card as varchar(50)),
          'CARD:9:' || new.id_card, 'CARD');
      end
    end
  end
end^

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
       and operation = 9
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
end^

set term ; ^

commit;
