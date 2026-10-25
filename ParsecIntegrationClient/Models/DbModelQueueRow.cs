namespace ParsecIntegrationClient.Models
{
    /// <summary>
    /// Строка очереди PARSEC_QUEUE. Числовые поля читаются из базы как есть,
    /// в DbModelRowIDInDev они преобразуются только на время вызова ParsecService.
    /// </summary>
    public sealed class DbModelQueueRow
    {
        public long ID { get; set; }
        public int OPERATION { get; set; }
        public int? ID_PEP { get; set; }
        public string ID_CARD { get; set; }
        public int ATTEMPTS { get; set; }
        public int MAX_ATTEMPTS { get; set; }
        public int STATUS { get; set; }

        public DbModelRowIDInDev ToLegacyRow()
        {
            return new DbModelRowIDInDev
            {
                ID = ID.ToString(),
                ID_CARD = string.IsNullOrWhiteSpace(ID_CARD) ? ID_CARD : ID_CARD.Trim(),
                ID_PEP = ID_PEP.HasValue ? ID_PEP.Value.ToString() : null,
                OPERATION = OPERATION.ToString(),
                ATTEMPS = ATTEMPTS.ToString()
            };
        }
    }
}
