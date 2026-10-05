namespace MiniPdm.Modules.BackgroundTasks.DtoModels;

/// <summary>
/// Описывает зарегистрированную фоновую задачу и её интервал запуска по умолчанию.
/// </summary>
public sealed record BackgroundTaskDefinitionRecordDto
{
    /// <summary>
    /// Уникальный идентификатор фоновой задачи.
    /// </summary>
    public string Id { get; init; } = default!;

    /// <summary>
    /// Отображаемое имя фоновой задачи.
    /// </summary>
    public string Name { get; init; } = default!;

    /// <summary>
    /// Интервал запуска задачи по умолчанию в минутах.
    /// </summary>
    public int DefaultIntervalMinutes
    {
        get; init;
    }
}

/// <summary>
/// Содержит сохранённое состояние фоновой задачи для отображения и планирования запусков.
/// </summary>
public sealed record BackgroundTaskRowDto
{
    /// <summary>
    /// Уникальный идентификатор фоновой задачи.
    /// </summary>
    public string Id { get; init; } = default!;

    /// <summary>
    /// Отображаемое имя фоновой задачи.
    /// </summary>
    public string Name { get; init; } = default!;

    /// <summary>
    /// Текущий интервал запуска задачи в минутах.
    /// </summary>
    public int IntervalMinutes
    {
        get; init;
    }

    /// <summary>
    /// Текущее состояние фоновой задачи.
    /// </summary>
    public string State { get; init; } = default!;

    /// <summary>
    /// Время следующего запланированного запуска или <see langword="null"/>, если запуск не запланирован.
    /// </summary>
    public DateTimeOffset? NextRunAt
    {
        get; init;
    }

    /// <summary>
    /// Время последнего начала выполнения или <see langword="null"/>, если задача ещё не запускалась.
    /// </summary>
    public DateTimeOffset? LastStartedAt
    {
        get; init;
    }

    /// <summary>
    /// Время последнего завершения выполнения или <see langword="null"/>, если выполнение ещё не завершалось.
    /// </summary>
    public DateTimeOffset? LastCompletedAt
    {
        get; init;
    }

    /// <summary>
    /// Результат последнего выполнения или <see langword="null"/>, если результата нет.
    /// </summary>
    public string? LastResult
    {
        get; init;
    }

    /// <summary>
    /// Описание последней ошибки или <see langword="null"/>, если ошибки нет.
    /// </summary>
    public string? LastError
    {
        get; init;
    }
}
