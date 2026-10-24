using System;
using System.ServiceModel;
using ParsecIntegrationClient.IntegrationWebService;

namespace ParsecIntegrationClient.Services
{
    /// <summary>
    /// SOAP-клиент Парсека. После перехода на .NET 8 это WCF-клиент, сгенерированный
    /// из WSDL Парсека (см. IntegrationWebService\IntegrationService.cs и скрипт
    /// tools\regenerate-parsec-client.ps1).
    ///
    /// Адрес сервиса и таймаут берутся из appsettings.json (Parsec:Url, Parsec:TimeoutSeconds):
    /// в сгенерированном клиенте адрес по умолчанию — лишь значение из WSDL, здесь оно
    /// не используется.
    /// </summary>
    public static class ParsecClient
    {
        private const int DefaultTimeoutSeconds = 100;

        /// <summary>
        /// Новый экземпляр клиента. Один экземпляр — один вызов/канал WCF.
        /// Создавайте его через <c>using</c>: Dispose освобождает канал и фабрику каналов.
        /// </summary>
        public static ParsecIntegrationServiceClient Create()
        {
            string url = GetUrl();

            return new ParsecIntegrationServiceClient(CreateBinding(url), new EndpointAddress(url));
        }

        /// <summary>
        /// Имя протокола для журнала. BasicHttpBinding всегда работает по SOAP 1.1,
        /// как и прежний прокси на SoapHttpClientProtocol.
        /// </summary>
        public static string ProtocolName()
        {
            return "SOAP 1.1";
        }

        internal static BasicHttpBinding CreateBinding(string url)
        {
            // Прежний прокси работал и по http, и по https; для https нужен транспортный режим.
            BasicHttpSecurityMode securityMode = IsHttps(url)
                ? BasicHttpSecurityMode.Transport
                : BasicHttpSecurityMode.None;

            BasicHttpBinding binding = new BasicHttpBinding(securityMode)
            {
                // Ответы Парсека (фото, события) не должны упираться в лимит 64 КБ по умолчанию.
                MaxBufferSize = int.MaxValue,
                MaxReceivedMessageSize = int.MaxValue,
                ReaderQuotas = System.Xml.XmlDictionaryReaderQuotas.Max,
                AllowCookies = true
            };

            int timeoutSeconds = SettingsService.ParsecTimeoutSeconds;
            if (timeoutSeconds <= 0)
            {
                timeoutSeconds = DefaultTimeoutSeconds;
            }

            // Прежний прокси выставлял только Timeout (общий); в WCF таймаутов четыре.
            TimeSpan timeout = TimeSpan.FromSeconds(timeoutSeconds);
            binding.OpenTimeout = timeout;
            binding.CloseTimeout = timeout;
            binding.SendTimeout = timeout;
            binding.ReceiveTimeout = timeout;

            return binding;
        }

        internal static string GetUrl()
        {
            string url = SettingsService.ParsecUrl;

            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException(
                    "Не задан адрес SOAP-сервиса Парсека (Parsec:Url в appsettings.json)");
            }

            return url.Trim();
        }

        private static bool IsHttps(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri)
                   && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Клиент WCF с безопасным Dispose. Обычный ClientBase.Dispose закрывает канал через
    /// Close(), а если SOAP-вызов уже завершился ошибкой, канал Faulted и Close() бросает
    /// исключение — оно замаскировало бы исходную ошибку. Здесь канал просто аварийно
    /// закрывается (Abort), что для короткоживущего клиента на один вызов достаточно.
    /// </summary>
    public sealed class ParsecIntegrationServiceClient : IntegrationServiceSoapClient, IDisposable
    {
        public ParsecIntegrationServiceClient(BasicHttpBinding binding, EndpointAddress address)
            : base(binding, address)
        {
        }

        void IDisposable.Dispose()
        {
            try
            {
                Abort();
            }
            catch
            {
                // Канал уже мог быть закрыт/аварийно завершён — освобождать больше нечего.
            }
        }
    }
}
