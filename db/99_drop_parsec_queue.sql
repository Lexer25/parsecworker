/* ============================================================================
   DROP everything created by scripts 01..03. Destroys history.
   Use only on a test database or for a clean re-install.
   ============================================================================ */

set term ^ ;

drop trigger parsec_q_ssaccess_ad^
drop trigger parsec_q_ssaccess_ai^
drop trigger parsec_q_card_ad^
drop trigger parsec_q_card_au^
drop trigger parsec_q_card_ai^
drop trigger parsec_q_org_ad^
drop trigger parsec_q_org_au^
drop trigger parsec_q_org_ai^
drop trigger parsec_q_people_ad^
drop trigger parsec_q_people_au^
drop trigger parsec_q_people_ai^

drop procedure parsec_enqueue^

drop table parsec_queue_log^
drop table parsec_queue^

drop generator gen_parsec_queue_log_id^
drop generator gen_parsec_queue_id^

set term ; ^

commit;
