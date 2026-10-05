using MiniPdm.Modules.BackgroundTasks.DtoModels;

namespace MiniPdm.Modules.BackgroundTasks.Abstractions.Database;

/// <summary>
/// Предоставляет операции чтения и изменения сохранённых определений и состояний фоновых задач.
/// </summary>
public interface IBackgroundTaskDatabaseService
{
    /// <summary>
    /// Создаёт отсутствующие записи задач и синхронизирует имена зарегистрированных задач.
    /// </summary>
    /// <param name="definitions">Набор зарегистрированных определений.</param>
    /// <param name="now">Текущее время для первоначального расписания.</param>
    /// <param name="ct">Токен отмены.</param>
    Task EnsureDefinitionsAsync(IReadOnlyCollection<BackgroundTaskDefinitionRecordDto> definitions, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Возвращает сохранённые состояния всех задач в порядке идентификатора.
    /// </summary>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Состояния задач.</returns>
    Task<IReadOnlyList<BackgroundTaskRowDto>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Находит сохранённое состояние задачи.
    /// </summary>
    /// <param name="id">Идентификатор задачи.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Состояние задачи или <see langword="null"/>, если запись отсутствует.</returns>
    Task<BackgroundTaskRowDto?> GetAsync(string id, CancellationToken ct);

    /// <summary>
    /// Сохраняет новый интервал запуска и пересчитывает следующий запуск, если задача не выполняется.
    /// </summary>
    /// <param name="id">Идентификатор задачи.</param>
    /// <param name="intervalMinutes">Интервал в минутах.</param>
    /// <param name="now">Текущее время.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись задачи найдена и обновлена.</returns>
    Task<bool> UpdateScheduleAsync(string id, int intervalMinutes, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Помечает задачу выполняющейся, если её запись существует и она ещё не выполняется.
    /// </summary>
    /// <param name="id">Идентификатор задачи.</param>
    /// <param name="startedAt">Время начала выполнения.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запуск зафиксирован.</returns>
    Task<bool> TryStartAsync(string id, DateTimeOffset startedAt, CancellationToken ct);

    /// <summary>
    /// Сохраняет конечное состояние и результат выполнения задачи.
    /// </summary>
    /// <param name="id">Идентификатор задачи.</param>
    /// <param name="state">Конечное состояние.</param>
    /// <param name="completedAt">Время завершения.</param>
    /// <param name="result">Краткий результат или <see langword="null"/>.</param>
    /// <param name="error">Описание ошибки или <see langword="null"/>.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись найдена и до этого имела состояние выполнения.</returns>
    Task<bool> CompleteAsync(string id, string state, DateTimeOffset completedAt, string? result, string? error, CancellationToken ct);
}
