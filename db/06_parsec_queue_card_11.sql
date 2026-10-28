/* ============================================================================
   Upgrade 4.0.0 -> 4.1.0: изменение свойств идентификатора, операция 11.

   Скрипт нужен на стендах, где уже выполнены 01..03 версии 4.0.0: в 4.0.0
   триггер PARSEC_Q_CARD_AU ставил только операцию 9 (добавить карту) и не видел
   ни ACTIVE, ни смену ID_CARDTYPE. Здесь триггеры CARD пересоздаются.

   Тело совпадает с db/03_parsec_queue_triggers.sql (там они создаются с нуля).
   Скрипт можно запускать повторно: если он оборвался между drop и create, повторный
   запуск даст безобидную ошибку "trigger not found" на уже удалённом триггере и
   создаст оба заново.

   Firebird 1.5 не умеет CREATE OR ALTER TRIGGER, поэтому drop + create.
   isql фиксирует каждый DDL-оператор сразу (AUTODDL включён по умолчанию): скрипт
   не атомарный, поэтому перед запуском остановите службу.
   ============================================================================ */

set term ^ ;

drop trigger parsec_q_card_au^
drop trigger parsec_q_card_ad^

/* Изменение карты. Операции:
   10        — ID_CARDTYPE перестал быть 1: идентификатор в Парсеке не нужен;
   9         — ID_CARDTYPE стал 1: идентификатор нужно добавить;
   10 и 9    — сменился владелец: старому удалить, новому добавить;
   11        — изменились TIMESTART/TIMEEND/ACTIVE: изменить свойства идентификатора.
   Пока идёт 9 или 11, событие склеивается по DEDUP_KEY, поэтому на одну карту в
   очереди всегда не больше одного незавершённого события каждого вида. */
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

set term ; ^

commit;