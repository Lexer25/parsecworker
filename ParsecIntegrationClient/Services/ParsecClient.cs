using System;
using System.Web.Services.Protocols;
using ParsecIntegrationClient.IntegrationWebService;

namespace ParsecIntegrationClient.Services
{
    /// <summary>
    /// SOAP-клиент Парсека. Адрес сервиса берётся из appsettings.json (Parsec:Url):
    /// раньше он был зашит в сгенерированный прокси и app.config и менялся только пересборкой.
    /// </summary>
    public static class ParsecClient
    {
        private const string UrlSettingName = "ParsecIntegrationClient_IntegrationWebService_IntegrationService";

        public static void Configure(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                // Значение по умолчанию у сгенерированного прокси подменяется до первого обращения к нему.
                Settings.Default.Properties[UrlSettingName].DefaultValue = url.Trim();
            }
            catch (Exception ex)
            {
                Logger.Log("ParsecClient", "Exception", "Не удалось применить Parsec:Url = " + url + ": " + ex.Message);
            }
        }

        /// <summary>Читаемое имя протокола: прокси по умолчанию работает по SOAP 1.1.</summary>
        public static string ProtocolName(SoapProtocolVersion version)
        {
            switch (version)
            {
                case SoapProtocolVersion.Soap11:
                    return "SOAP 1.1";
                case SoapProtocolVersion.Soap12:
                    return "SOAP 1.2";
                default:
                    return "SOAP 1.1 (по умолчанию)";
            }
        }

        public static IntegrationService Create()
        {
            IntegrationService client = new IntegrationService();

            int timeoutSeconds = SettingsService.ParsecTimeoutSeconds;
            if (timeoutSeconds > 0)
            {
                client.Timeout = timeoutSeconds * 1000;
            }

            return client;
        }
    }
}

