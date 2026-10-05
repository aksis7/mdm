namespace MiniPdm.Desktop.Services;

/// <summary>
/// Содержит параметры подключения настольного клиента к API PDM.
/// </summary>
public sealed record PdmApiClientOptions(Uri BaseAddress)
{
    /// <summary>
    /// Возвращает абсолютный базовый адрес API с завершающим слешем.
    /// </summary>
    public Uri BaseAddress { get; init; } = BaseAddress;

    /// <summary>
    /// Создаёт настройки из переменной окружения <c>PDM_API_BASE_URL</c>.
    /// </summary>
    /// <returns>Настройки с проверенным базовым адресом; при отсутствии переменной используется <c>http://localhost:5000/</c>.</returns>
    public static PdmApiClientOptions FromEnvironment()
    {
        var configured = Environment.GetEnvironmentVariable("PDM_API_BASE_URL");
        var value = string.IsNullOrWhiteSpace(configured) ? "http://localhost:5000" : configured.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var address) ||
            !(string.Equals(address.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
        {
            throw new InvalidOperationException(
                "PDM_API_BASE_URL must be an absolute HTTP or HTTPS URL without a query or fragment.");
        }

        var baseAddress = address.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? address
            : new Uri(address.AbsoluteUri + "/", UriKind.Absolute);
        return new PdmApiClientOptions(baseAddress);
    }
}
