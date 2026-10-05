using MiniPdm.Common.Exceptions;
using Resources = MiniPdm.Common.Resources;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MiniPdm.Contracts.Modules.BackgroundTasks.DtoModels;
using MiniPdm.Modules.BackgroundTasks.Abstractions;
using MiniPdm.Modules.BackgroundTasks.Features.Queries.ListBackgroundTasks;
using MiniPdm.Modules.BackgroundTasks.Features.Commands.RunBackgroundTask;
using MiniPdm.Modules.BackgroundTasks.Features.Commands.UpdateBackgroundTaskSchedule;

namespace MiniPdm.Modules.BackgroundTasks.Controllers;

/// <summary>
/// Предоставляет HTTP-операции для просмотра, настройки и ручного запуска фоновых задач.
/// </summary>
/// <param name="sender">Отправитель запросов MediatR.</param>
[ApiController]
[Route("api/background-tasks")]
public sealed class BackgroundTasksController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Возвращает список фоновых задач.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса.</param>
    /// <returns>Ответ 200 со списком задач либо 503 при недоступности хранилища.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BackgroundTaskDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await sender.Send(new ListBackgroundTasksQuery(), cancellationToken));
        }
        catch (BackgroundTaskPersistenceUnavailableException ex) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message }); }
    }

    /// <summary>
    /// Изменяет интервал запуска фоновой задачи.
    /// </summary>
    /// <param name="taskId">Идентификатор задачи.</param>
    /// <param name="request">Запрос с новым интервалом в минутах.</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса.</param>
    /// <returns>Ответ 200 с обновлённой задачей, 400 при недопустимом интервале, 404 при отсутствии задачи или 503 при недоступности хранилища.</returns>
    [HttpPut("{taskId}/schedule")]
    [ProducesResponseType(typeof(BackgroundTaskDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSchedule(string taskId, UpdateBackgroundTaskScheduleRequestDto request, CancellationToken cancellationToken)
    {
        if (request.IntervalMinutes is < 1 or > 525600)
            throw new InputLogicException(Resources.InputLogicException.IntervalRange);
        try
        {
            var result = await sender.Send(new UpdateBackgroundTaskScheduleCommand(taskId, request.IntervalMinutes), cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (BackgroundTaskPersistenceUnavailableException ex) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message }); }
    }

    /// <summary>
    /// Запрашивает немедленный запуск фоновой задачи.
    /// </summary>
    /// <param name="taskId">Идентификатор задачи.</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса.</param>
    /// <returns>Ответ 202 при принятии, 404 при отсутствии задачи, 409 если задача уже выполняется или 503 при недоступности хранилища.</returns>
    [HttpPost("{taskId}/run")]
    [ProducesResponseType(typeof(BackgroundTaskRunAcceptedDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Run(string taskId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await sender.Send(new RunBackgroundTaskCommand(taskId), cancellationToken);
            return result.Status switch
            {
                BackgroundTaskRunRequestStatus.Accepted => Accepted(value: new BackgroundTaskRunAcceptedDto
                {
                    TaskId = taskId
                }),
                BackgroundTaskRunRequestStatus.Running => throw new BusinessLogicException(Resources.BusinessLogicException.TaskAlreadyRunning),
                _ => NotFound()
            };
        }
        catch (BackgroundTaskPersistenceUnavailableException ex) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message }); }
    }
}
