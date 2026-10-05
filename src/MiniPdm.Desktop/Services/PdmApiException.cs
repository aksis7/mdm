namespace MiniPdm.Desktop.Services;

/// <summary>
/// Представляет ошибку, возвращённую API PDM, вместе с диагностическими данными ответа.
/// </summary>
public sealed class PdmApiException : Exception
{
    /// <summary>
    /// Создаёт исключение для ответа API.
    /// </summary>
    /// <param name="statusCode">HTTP-код ответа.</param>
    /// <param name="message">Сообщение ошибки.</param>
    /// <param name="errorCode">Машиночитаемый код ошибки, если сервер его передал.</param>
    /// <param name="cyclePath">Путь объектов, образующих цикл, если он присутствует в ответе.</param>
    /// <param name="responseBody">Исходное тело ответа для диагностики.</param>
    public PdmApiException(int statusCode, string message, string? errorCode = null,
        IReadOnlyList<Guid>? cyclePath = null, string? responseBody = null)
        : base(FormatMessage(message, cyclePath))
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        CyclePath = cyclePath;
        ResponseBody = responseBody;
    }

    /// <summary>
    /// Возвращает HTTP-код, полученный от сервера.
    /// </summary>
    public int StatusCode
    {
        get;
    }

    /// <summary>
    /// Возвращает код ошибки API или <see langword="null"/>, если он не был указан.
    /// </summary>
    public string? ErrorCode
    {
        get;
    }

    /// <summary>
    /// Возвращает последовательность идентификаторов цикла состава, если она известна.
    /// </summary>
    public IReadOnlyList<Guid>? CyclePath
    {
        get;
    }

    /// <summary>
    /// Возвращает исходное тело ответа сервера, если оно доступно.
    /// </summary>
    public string? ResponseBody
    {
        get;
    }

    /// <summary>
    /// Показывает, что для ошибки HTTP 503 итог команды изменения мог остаться неизвестен клиенту.
    /// </summary>
    public bool IsOutcomeUnknown => StatusCode == 503;

    private static string FormatMessage(string message, IReadOnlyList<Guid>? cyclePath)
    {
        if (cyclePath is not { Count: > 0 })
            return message;
        return $"{message} Cycle: {string.Join(" → ", cyclePath)}";
    }
}
