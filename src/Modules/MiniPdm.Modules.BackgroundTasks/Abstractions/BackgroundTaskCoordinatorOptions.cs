namespace MiniPdm.Modules.BackgroundTasks.Abstractions;

/// <summary>
/// Настраивает поведение координатора фоновых задач.
/// </summary>
public sealed record BackgroundTaskCoordinatorOptions(TimeSpan CompletionWriteRetryDelay)
{
    /// <summary>
    /// Задержка перед повторной записью результата завершённой задачи.
    /// </summary>
    public TimeSpan CompletionWriteRetryDelay { get; init; } = CompletionWriteRetryDelay;

    /// <summary>
    /// Параметры координатора по умолчанию с задержкой повтора записи в 30 секунд.
    /// </summary>
    public static BackgroundTaskCoordinatorOptions Default { get; } = new(TimeSpan.FromSeconds(30));
}
