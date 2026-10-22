using ParsecIntegrationClient.Services;
using System;

namespace ParsecIntegrationClient.Models
{
    public static class License
    {
        private static bool _isLicensed = false;
        private static int _transactionCounter = 0;
        private static readonly int MaxFreeTransactions = 10;
        private static bool _isBlocked = false;

        public static bool IsLicensed => _isLicensed;
        public static int RemainingTransactions => Math.Max(0, MaxFreeTransactions - _transactionCounter);
        public static bool IsBlocked => _isBlocked;

        public static void SetLicenseStatus(bool licensed)
        {
            _isLicensed = licensed;
            if (licensed)
            {
                _isBlocked = false;
                _transactionCounter = 0;
                Logger.Log("License", "Info", $"24 Лицензия активна, все функции доступны");
            }
            else
            {
                Logger.Log("License", "Warning", $"28 Лицензия не найдена, доступно {RemainingTransactions} транзакций из {MaxFreeTransactions}");
            }
        }

        public static bool CanPerformOperation()
        {
            if (_isBlocked)
            {
                Logger.Log("License", "Warning", "36 Операции заблокированы - превышен лимит бесплатных транзакций");
                return false;
            }

            if (_isLicensed)
                return true;

            if (_transactionCounter < MaxFreeTransactions)
            {
                _transactionCounter++;
                Logger.Log("License", "Info", $"46 Выполняется транзакция {_transactionCounter}/{MaxFreeTransactions}. Осталось: {MaxFreeTransactions - _transactionCounter}");
                return true;
            }

            _isBlocked = true;
            Logger.Log("License", "Error", $"51 Достигнут лимит бесплатных транзакций ({MaxFreeTransactions}). Программа заблокирована.");
            return false;
        }

        public static void ResetTransactionCounter()
        {
            if (!_isLicensed)
            {
                _transactionCounter = 0;
                _isBlocked = false;
                Logger.Log("License", "Info", $"Счетчик транзакций сброшен. Доступно {MaxFreeTransactions} транзакций.");
            }
        }
    }
}
