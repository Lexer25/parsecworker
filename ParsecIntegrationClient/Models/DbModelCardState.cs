using System;

namespace ParsecIntegrationClient.Models
{
    /// <summary>
    /// Состояние карты в базе СКУД Артонит, по которому формируется идентификатор
    /// Парсека: срок действия, тип карты и признак активности. Читается одним
    /// запросом в <see cref="Services.DatabaseService.TryGetCardState"/>.
    ///
    /// Наличие срока действия определяет тип объекта в SOAP: тип выбирается по
    /// TIMEEND, а не по TIMESTART (ТЗ 4.3, таблица выбора).
    /// </summary>
    public sealed class DbModelCardState
    {
        /// <summary>Номер карты (CARD.ID_CARD), как он записан в базе.</summary>
        public string ID_CARD { get; set; }

        /// <summary>Владелец карты (CARD.ID_PEP).</summary>
        public int? ID_PEP { get; set; }

        /// <summary>
        /// GUID человека в Парсеке (PEOPLE.GUID). Пусто, если карта привязана к
        /// человеку без GUID: такую карту в Парсек передать нельзя.
        /// </summary>
        public string GUID_PEP { get; set; }

        /// <summary>
        /// Тип карты (CARD.ID_CARDTYPE). В Парсек передаются только карты типа 1,
        /// у остальных идентификатор в Парсеке не нужен (ТЗ 4.3, п. 5-6).
        /// </summary>
        public int? ID_CARDTYPE { get; set; }

        /// <summary>Начало срока действия (CARD.TIMESTART). null — не задано.</summary>
        public DateTime? TIME_START { get; set; }

        /// <summary>Окончание срока действия (CARD.TIMEEND). null — не задано.</summary>
        public DateTime? TIME_END { get; set; }

        /// <summary>
        /// Признак активности карты (CARD.ACTIVE): 1 — активна, 0 — заблокирована.
        /// Передаётся в Парсек как IdentifierExData.ENTRY_LIMIT: -1 или 0.
        /// </summary>
        public int? ACTIVE { get; set; }

        /// <summary>Карта, которую нужно передавать в Парсек (ID_CARDTYPE = 1).</summary>
        public bool IsAccessCard
        {
            get { return ID_CARDTYPE.HasValue && ID_CARDTYPE.Value == 1; }
        }
    }
}
