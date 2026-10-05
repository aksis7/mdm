using MediatR;
using MiniPdm.Modules.BackgroundTasks.Abstractions;

namespace MiniPdm.Modules.BackgroundTasks.Features.Commands.RunBackgroundTask;

/// <summary>
/// Представляет запрос на немедленный запуск фоновой задачи.
/// </summary>
public sealed record RunBackgroundTaskCommand(string TaskId) : IRequest<BackgroundTaskRunRequestResult>
{
    /// <summary>
    /// Идентификатор задачи, для которой запрашивается запуск.
    /// </summary>
    public string TaskId { get; init; } = TaskId;
}

/// <summary>
/// Передаёт запрос ручного запуска координатору.
/// </summary>
/// <param name="coordinator">Координатор фоновых задач.</param>
public sealed class RunBackgroundTaskCommandHandler(IBackgroundTaskCoordinator coordinator)
    : IRequestHandler<RunBackgroundTaskCommand, BackgroundTaskRunRequestResult>
{
    /// <summary>
    /// Обрабатывает запрос ручного запуска.
    /// </summary>
    /// <param name="request">Запрос с идентификатором задачи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Статус принятия запуска.</returns>
    public Task<BackgroundTaskRunRequestResult> Handle(RunBackgroundTaskCommand request, CancellationToken cancellationToken) =>
        coordinator.RequestManualRunAsync(request.TaskId, cancellationToken);
}
