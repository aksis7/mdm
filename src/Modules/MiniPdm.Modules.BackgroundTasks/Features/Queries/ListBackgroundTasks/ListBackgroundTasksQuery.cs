using MediatR;
using MiniPdm.Contracts.Modules.BackgroundTasks.DtoModels;
using MiniPdm.Modules.BackgroundTasks.Abstractions;

namespace MiniPdm.Modules.BackgroundTasks.Features.Queries.ListBackgroundTasks;

/// <summary>
/// Представляет запрос на получение списка фоновых задач.
/// </summary>
public sealed record ListBackgroundTasksQuery : IRequest<IReadOnlyList<BackgroundTaskDto>>;

/// <summary>
/// Получает список задач через координатор.
/// </summary>
/// <param name="coordinator">Координатор фоновых задач.</param>
public sealed class ListBackgroundTasksQueryHandler(IBackgroundTaskCoordinator coordinator)
    : IRequestHandler<ListBackgroundTasksQuery, IReadOnlyList<BackgroundTaskDto>>
{
    /// <summary>
    /// Обрабатывает запрос списка задач.
    /// </summary>
    /// <param name="request">Запрос списка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список задач с расписанием и последними результатами.</returns>
    public Task<IReadOnlyList<BackgroundTaskDto>> Handle(ListBackgroundTasksQuery request, CancellationToken cancellationToken) =>
        coordinator.ListAsync(cancellationToken);
}
