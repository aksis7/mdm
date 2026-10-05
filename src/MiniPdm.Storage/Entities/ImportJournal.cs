namespace MiniPdm.Storage.Entities;

/// <summary>
///     Запись идемпотентности с отчётом о завершённом запросе импорта.
/// </summary>
public sealed class ImportJournal
{
    /// <summary>
    ///     Возвращает или задаёт идентификатор операции импорта.
    /// </summary>
    public Guid ImportId
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт время завершения импорта.
    /// </summary>
    public DateTimeOffset CompletedAt
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт сериализованный отчёт импорта для повторных запросов.
    /// </summary>
    public string ReportJson { get; set; } = "";
}
