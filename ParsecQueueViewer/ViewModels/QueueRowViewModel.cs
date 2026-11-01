using System.Globalization;
using ParsecQueueViewer.Models;

namespace ParsecQueueViewer.ViewModels
{
    public sealed class QueueRowViewModel
    {
        private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        private readonly string _searchText;

        public QueueRowViewModel(QueueRow row)
        {
            Model = row ?? throw new ArgumentNullException(nameof(row));

            Id = row.Id;
            Operation = row.Operation;
            IdPep = row.IdPep;
            IdCard = row.IdCard;
            Status = row.Status;
            Attempts = row.Attempts;
            MaxAttempts = row.MaxAttempts;
            ErrorCode = row.ErrorCode;
            ErrorText = row.ErrorText;
            DedupKey = row.DedupKey;
            WorkerId = row.WorkerId;
            SourceTable = row.SourceTable;
            NextAttempt = row.NextAttempt;
            LeaseUntil = row.LeaseUntil;
            CreatedAt = row.CreatedAt;
            UpdatedAt = row.UpdatedAt;
            StartedAt = row.StartedAt;
            FinishedAt = row.FinishedAt;

            StatusName = GetStatusName(row.Status);
            StatusDescription = GetStatusDescription(row.Status);
            OperationName = GetOperationName(row.Operation);
            OperationCodeDisplay = "операция " + row.Operation.ToString(CultureInfo.InvariantCulture);
            (ObjectTitle, ObjectSubtitle) = GetObject(row);

            AttemptsDisplay = row.MaxAttempts > 0
                ? row.Attempts.ToString(CultureInfo.InvariantCulture) + " / " +
                  row.MaxAttempts.ToString(CultureInfo.InvariantCulture)
                : row.Attempts.ToString(CultureInfo.InvariantCulture);

            CreatedDisplay = FormatDate(row.CreatedAt);
            UpdatedDisplay = FormatDate(row.UpdatedAt);
            StartedDisplay = FormatDate(row.StartedAt);
            FinishedDisplay = FormatDate(row.FinishedAt);
            NextAttemptDisplay = FormatDate(row.NextAttempt);
            LeaseUntilDisplay = FormatDate(row.LeaseUntil);
            DurationDisplay = FormatDuration(row.DurationMs);
            ErrorDisplay = string.IsNullOrWhiteSpace(row.ErrorText) ? "—" : row.ErrorText;
            ErrorCodeDisplay = row.ErrorCode.HasValue
                ? row.ErrorCode.Value.ToString(CultureInfo.InvariantCulture)
                : "—";
            WorkerDisplay = string.IsNullOrWhiteSpace(row.WorkerId) ? "—" : row.WorkerId;
            SourceDisplay = string.IsNullOrWhiteSpace(row.SourceTable) ? "—" : row.SourceTable;
            DedupDisplay = string.IsNullOrWhiteSpace(row.DedupKey) ? "—" : row.DedupKey;
            IdPepDisplay = row.IdPep.HasValue
                ? row.IdPep.Value.ToString(CultureInfo.InvariantCulture)
                : "—";
            IdCardDisplay = string.IsNullOrWhiteSpace(row.IdCard) ? "—" : row.IdCard.Trim();
            IsActive = row.Status is 0 or 1 or 3 or 4;

            _searchText = string.Join(" ",
                Id.ToString(CultureInfo.InvariantCulture),
                StatusName,
                StatusDescription,
                OperationName,
                OperationCodeDisplay,
                ObjectTitle,
                ObjectSubtitle,
                IdPepDisplay,
                IdCardDisplay,
                AttemptsDisplay,
                ErrorCodeDisplay,
                ErrorText,
                DedupDisplay,
                WorkerDisplay,
                SourceDisplay).ToLowerInvariant();
        }

        public QueueRow Model { get; }

        public long Id { get; }
        public int Operation { get; }
        public int? IdPep { get; }
        public string IdCard { get; }
        public int Status { get; }
        public int Attempts { get; }
        public int MaxAttempts { get; }
        public int? ErrorCode { get; }
        public string ErrorText { get; }
        public string DedupKey { get; }
        public string WorkerId { get; }
        public string SourceTable { get; }
        public DateTime? NextAttempt { get; }
        public DateTime? LeaseUntil { get; }
        public DateTime? CreatedAt { get; }
        public DateTime? UpdatedAt { get; }
        public DateTime? StartedAt { get; }
        public DateTime? FinishedAt { get; }
        public bool IsActive { get; }

        public string StatusName { get; }
        public string StatusDescription { get; }
        public string OperationName { get; }
        public string OperationCodeDisplay { get; }
        public string ObjectTitle { get; }
        public string ObjectSubtitle { get; }
        public string AttemptsDisplay { get; }
        public string CreatedDisplay { get; }
        public string UpdatedDisplay { get; }
        public string StartedDisplay { get; }
        public string FinishedDisplay { get; }
        public string NextAttemptDisplay { get; }
        public string LeaseUntilDisplay { get; }
        public string DurationDisplay { get; }
        public string ErrorDisplay { get; }
        public string ErrorCodeDisplay { get; }
        public string WorkerDisplay { get; }
        public string SourceDisplay { get; }
        public string DedupDisplay { get; }
        public string IdPepDisplay { get; }
        public string IdCardDisplay { get; }

        public string DetailsTitle => "Событие #" + Id.ToString(CultureInfo.InvariantCulture);

        public string SearchText => _searchText;

        public static string GetStatusName(int status)
        {
            return status switch
            {
                0 => "Новая",
                1 => "В работе",
                2 => "Успешно",
                3 => "Повтор",
                4 => "Ошибка",
                5 => "Пропущено",
                6 => "Отменено",
                _ => "Статус " + status.ToString(CultureInfo.InvariantCulture)
            };
        }

        public static string GetStatusDescription(int status)
        {
            return status switch
            {
                0 => "Ожидает обработки интегратором",
                1 => "Строка забрана в работу",
                2 => "Обработана успешно",
                3 => "Ошибка, будет повторная попытка",
                4 => "Попытки исчерпаны, автоматических повторов не будет",
                5 => "Пропущена без обращения в Парсек",
                6 => "Отменена: объект удалён или перепривязан",
                _ => "Неизвестный статус"
            };
        }

        public static string GetOperationName(int operation)
        {
            return operation switch
            {
                3 => "Добавить сотрудника",
                35 => "Изменить сотрудника",
                4 => "Удалить сотрудника",
                5 => "Добавить организацию",
                55 => "Изменить организацию",
                6 => "Удалить организацию",
                7 => "Выдать категорию доступа",
                8 => "Снять категорию доступа",
                9 => "Добавить карту",
                10 => "Удалить карту",
                11 => "Изменить свойства карты",
                _ => "Неизвестная операция"
            };
        }

        private static (string Title, string Subtitle) GetObject(QueueRow row)
        {
            string person = row.IdPep.HasValue
                ? "Сотрудник #" + row.IdPep.Value.ToString(CultureInfo.InvariantCulture)
                : "Сотрудник";

            string card = string.IsNullOrWhiteSpace(row.IdCard) ? "—" : row.IdCard.Trim();
            string guid = string.IsNullOrWhiteSpace(row.IdCard) ? "—" : row.IdCard.Trim();

            switch (row.Operation)
            {
                case 3:
                case 4:
                case 35:
                    return (person, "ID_PEP: " + (row.IdPep?.ToString(CultureInfo.InvariantCulture) ?? "—"));

                case 5:
                case 6:
                case 55:
                    return ("Организация", "ID_CARD: " + guid);

                case 7:
                case 8:
                    return ("Категория доступа", BuildAccessCategorySubtitle(person, row.DedupKey));

                case 9:
                case 10:
                case 11:
                    return ("Карта " + card, person);

                default:
                    return ("Объект", "ID_PEP: " + (row.IdPep?.ToString(CultureInfo.InvariantCulture) ?? "—"));
            }
        }

        private static string BuildAccessCategorySubtitle(string person, string dedupKey)
        {
            if (string.IsNullOrWhiteSpace(dedupKey))
            {
                return person;
            }

            string[] parts = dedupKey.Split(':');
            if (parts.Length >= 4 && int.TryParse(parts[^1], out int accessName))
            {
                return person + " · категория #" + accessName.ToString(CultureInfo.InvariantCulture);
            }

            return person;
        }

        private static string FormatDate(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("dd.MM.yyyy HH:mm:ss", Ru) : "—";
        }

        public static string FormatDuration(int? milliseconds)
        {
            if (!milliseconds.HasValue || milliseconds.Value < 0)
            {
                return "—";
            }

            int ms = milliseconds.Value;
            if (ms < 1000)
            {
                return ms.ToString(CultureInfo.InvariantCulture) + " мс";
            }

            double seconds = ms / 1000d;
            if (seconds < 60)
            {
                return seconds.ToString("0.0", Ru) + " с";
            }

            TimeSpan duration = TimeSpan.FromMilliseconds(ms);
            if (duration.TotalHours >= 1)
            {
                return ((int)duration.TotalHours).ToString(CultureInfo.InvariantCulture) + " ч " +
                       duration.Minutes.ToString("00", CultureInfo.InvariantCulture) + " мин";
            }

            return duration.Minutes.ToString(CultureInfo.InvariantCulture) + " мин " +
                   duration.Seconds.ToString("00", CultureInfo.InvariantCulture) + " с";
        }
    }
}
