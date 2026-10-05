using System.ComponentModel.DataAnnotations;

namespace MiniPdm.Contracts.Modules.BackgroundTasks.DtoModels;

/// <summary>
/// Представляет сведения о состоянии, расписании и последнем выполнении фоновой задачи в API.
/// </summary>
public sealed record BackgroundTaskDto
{
    /// <summary>
    /// Уникальный идентификатор задачи.
    /// </summary>
    public string Id { get; init; } = default!;

    /// <summary>
    /// Отображаемое имя задачи.
    /// </summary>
    public string Name { get; init; } = default!;

    /// <summary>
    /// Текущий интервал запуска в минутах.
    /// </summary>
    public int IntervalMinutes
    {
        get; init;
    }

    /// <summary>
    /// Текущее состояние задачи.
    /// </summary>
    public string State { get; init; } = default!;

    /// <summary>
    /// Время следующего запуска либо <see langword="null"/>, если он не запланирован.
    /// </summary>
    public DateTimeOffset? NextRunAt
    {
        get; init;
    }

    /// <summary>
    /// Время последнего начала либо <see langword="null"/>, если задача ещё не запускалась.
    /// </summary>
    public DateTimeOffset? LastStartedAt
    {
        get; init;
    }

    /// <summary>
    /// Время последнего завершения либо <see langword="null"/>, если выполнение ещё не завершалось.
    /// </summary>
    public DateTimeOffset? LastCompletedAt
    {
        get; init;
    }

    /// <summary>
    /// Краткий результат последнего выполнения либо <see langword="null"/>.
    /// </summary>
    public string? LastResult
    {
        get; init;
    }

    /// <summary>
    /// Описание ошибки последнего выполнения либо <see langword="null"/>, если ошибки нет.
    /// </summary>
    public string? LastError
    {
        get; init;
    }
}

/// <summary>
/// Содержит интервал запуска задачи, передаваемый при изменении расписания.
/// </summary>
public sealed record UpdateBackgroundTaskScheduleRequestDto
{
    /// <summary>
    /// Новый интервал запуска в минутах; допустимый диапазон — от 1 до 525600.
    /// </summary>
    [Range(1, 525600, ErrorMessageResourceType = typeof(MiniPdm.Common.Resources.InputLogicException),
        ErrorMessageResourceName = nameof(MiniPdm.Common.Resources.InputLogicException.RangeOneTo525600))]
    public int IntervalMinutes
    {
        get; init;
    }
}

/// <summary>
/// Подтверждает принятие запроса на ручной запуск фоновой задачи.
/// </summary>
public sealed record BackgroundTaskRunAcceptedDto
{
    /// <summary>
    /// Идентификатор задачи, для которой принят запуск.
    /// </summary>
    public string TaskId { get; init; } = default!;

}
