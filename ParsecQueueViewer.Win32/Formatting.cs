using System;
using System.Globalization;
using System.Text;

namespace ParsecQueueViewer.Win32
{
    internal static class Formatting
    {
        private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

        public static string StatusName(int status)
        {
            switch (status)
            {
                case 0: return "Новая";
                case 1: return "В работе";
                case 2: return "Успешно";
                case 3: return "Повтор";
                case 4: return "Ошибка";
                case 5: return "Пропущено";
                case 6: return "Отменено";
                default: return "Статус " + status.ToString(CultureInfo.InvariantCulture);
            }
        }

        public static string StatusDescription(int status)
        {
            switch (status)
            {
                case 0: return "Ожидает обработки интегратором";
                case 1: return "Строка забрана в работу";
                case 2: return "Обработана успешно";
                case 3: return "Ошибка, будет повторная попытка";
                case 4: return "Попытки исчерпаны, автоматических повторов не будет";
                case 5: return "Пропущена без обращения в Парсек";
                case 6: return "Отменена: объект удалён или перепривязан";
                default: return "Неизвестный статус";
            }
        }

        public static string OperationName(int operation)
        {
            switch (operation)
            {
                case 3: return "Добавить сотрудника";
                case 35: return "Изменить сотрудника";
                case 4: return "Удалить сотрудника";
                case 5: return "Добавить организацию";
                case 55: return "Изменить организацию";
                case 6: return "Удалить организацию";
                case 7: return "Выдать категорию доступа";
                case 8: return "Снять категорию доступа";
                case 9: return "Добавить карту";
                case 10: return "Удалить карту";
                case 11: return "Изменить свойства карты";
                default: return "Неизвестная операция";
            }
        }

        public static void Object(QueueRow row, out string title, out string subtitle)
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
                    title = person;
                    subtitle = "ID_PEP: " + (row.IdPep?.ToString(CultureInfo.InvariantCulture) ?? "—");
                    return;

                case 5:
                case 6:
                case 55:
                    title = "Организация";
                    subtitle = "ID_CARD: " + guid;
                    return;

                case 7:
                case 8:
                    title = "Категория доступа";
                    subtitle = person + AccessCategorySuffix(row.DedupKey);
                    return;

                case 9:
                case 10:
                case 11:
                    title = "Карта " + card;
                    subtitle = person;
                    return;

                default:
                    title = "Объект";
                    subtitle = "ID_PEP: " + (row.IdPep?.ToString(CultureInfo.InvariantCulture) ?? "—");
                    return;
            }
        }

        private static string AccessCategorySuffix(string dedupKey)
        {
            if (string.IsNullOrWhiteSpace(dedupKey))
            {
                return string.Empty;
            }

            string[] parts = dedupKey.Split(':');
            int accessName;
            if (parts.Length >= 4 && int.TryParse(parts[parts.Length - 1], out accessName))
            {
                return " · категория #" + accessName.ToString(CultureInfo.InvariantCulture);
            }

            return string.Empty;
        }

        public static string Date(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("dd.MM.yyyy HH:mm:ss", Ru) : "—";
        }

        public static string Time(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "—";
        }

        public static string Duration(int? milliseconds)
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

        public static string Attempts(QueueRow row)
        {
            return row.MaxAttempts > 0
                ? row.Attempts.ToString(CultureInfo.InvariantCulture) + " / " +
                  row.MaxAttempts.ToString(CultureInfo.InvariantCulture)
                : row.Attempts.ToString(CultureInfo.InvariantCulture);
        }

        public static string BuildSearchText(QueueRow row, string operation, string objectTitle,
            string objectSubtitle)
        {
            StringBuilder text = new StringBuilder(256);
            text.Append(row.Id.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(StatusName(row.Status)).Append(' ')
                .Append(StatusDescription(row.Status)).Append(' ')
                .Append(operation).Append(' ')
                .Append("операция ").Append(row.Operation.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(objectTitle).Append(' ')
                .Append(objectSubtitle).Append(' ')
                .Append(row.IdPep?.ToString(CultureInfo.InvariantCulture) ?? "—").Append(' ')
                .Append(row.IdCard ?? "—").Append(' ')
                .Append(Attempts(row)).Append(' ')
                .Append(row.ErrorCode?.ToString(CultureInfo.InvariantCulture) ?? "—").Append(' ')
                .Append(row.ErrorText ?? string.Empty).Append(' ')
                .Append(row.DedupKey ?? "—").Append(' ')
                .Append(row.WorkerId ?? "—").Append(' ')
                .Append(row.SourceTable ?? "—");
            return text.ToString().ToLowerInvariant();
        }
    }
}
