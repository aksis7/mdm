namespace MiniPdm.Storage.Entities;

/// <summary>
///     Сохранённое расписание, состояние выполнения и последний результат фоновой задачи.
/// </summary>
public sealed class BackgroundTask
{
    /// <summary>
    ///     Возвращает или задаёт постоянный ключ задачи.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     Возвращает или задаёт отображаемое имя задачи.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Возвращает или задаёт интервал запуска в минутах.
    /// </summary>
    public int IntervalMinutes
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт состояние задачи, например Idle, Running или Failed.
    /// </summary>
    public string State { get; set; } = "Idle";

    /// <summary>
    ///     Возвращает или задаёт время следующего запуска.
    /// </summary>
    public DateTimeOffset? NextRunAt
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт время последнего запуска.
    /// </summary>
    public DateTimeOffset? LastStartedAt
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт время последнего завершения.
    /// </summary>
    public DateTimeOffset? LastCompletedAt
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт описание последнего успешного или частичного результата.
    /// </summary>
    public string? LastResult
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт описание последней ошибки.
    /// </summary>
    public string? LastError
    {
        get; set;
    }
}
