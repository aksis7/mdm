using MiniPdm.Common.Exceptions;
using Resources = MiniPdm.Common.Resources;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MiniPdm.Contracts.Modules.Versions.DtoModels;
using MiniPdm.Domain.Versions;
using MiniPdm.Domain.Versions.Mutations;
using MiniPdm.Modules.Versions.Features.Commands.ChangeVersionState;
using MiniPdm.Modules.Versions.Features.Commands.CloneVersion;
using MiniPdm.Modules.Versions.Services;

namespace MiniPdm.Modules.Versions.Controllers;

/// <summary>
/// Предоставляет HTTP-операции создания версий и изменения их состояния.
/// </summary>
/// <param name="sender">Посредник команд приложения.</param>
[ApiController]
[Route("api/objects")]
public sealed class VersionsController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Создаёт версию, клонируя указанную исходную версию объекта.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта в маршруте.</param>
    /// <param name="request">Исходная версия и ожидаемый токен объекта.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>HTTP-ответ с созданной версией либо описанием ошибки проверки или конфликта.</returns>
    [HttpPost("{objectId:guid}/versions")]
    public async Task<IActionResult> Clone(Guid objectId, [FromBody] CloneVersionRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        if (objectId == Guid.Empty)
            throw new InputLogicException(Resources.InputLogicException.ObjectIdRequired);
        if (request is null)
            throw new InputLogicException(Resources.InputLogicException.RequestBodyRequired);
        if (request.SourceVersion <= 0)
            throw new InputLogicException(Resources.InputLogicException.SourceVersionPositive);
        if (request.ExpectedConcurrencyToken == Guid.Empty)
            throw new InputLogicException(Resources.InputLogicException.ConcurrencyTokenRequired);

        VersionMutationResult result;
        try
        {
            result = await sender.Send(new CloneVersionCommand(objectId, request.SourceVersion,
                request.ExpectedConcurrencyToken), cancellationToken);
        }
        catch (VersionWriteUncertainException)
        {
            return UncertainWrite();
        }

        var response = Map(result);
        if (response is not null)
            return response;
        var dto = ToDto(result);
        return Created($"/api/objects/{result.ObjectId}?version={result.VersionNumber}", dto);
    }

    /// <summary>
    /// Меняет состояние заданной версии объекта.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта в маршруте.</param>
    /// <param name="version">Номер версии в маршруте.</param>
    /// <param name="request">Новое состояние и ожидаемый токен объекта.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>HTTP-ответ с результатом операции или описанием ошибки.</returns>
    [HttpPut("{objectId:guid}/versions/{version:int}/state")]
    public async Task<IActionResult> ChangeState(Guid objectId, int version,
        [FromBody] ChangeVersionStateRequestDto? request, CancellationToken cancellationToken = default)
    {
        if (objectId == Guid.Empty)
            throw new InputLogicException(Resources.InputLogicException.ObjectIdRequired);
        if (version <= 0)
            throw new InputLogicException(Resources.InputLogicException.VersionMustBePositive);
        if (request is null)
            throw new InputLogicException(Resources.InputLogicException.RequestBodyRequired);
        if (request.ExpectedConcurrencyToken == Guid.Empty)
            throw new InputLogicException(Resources.InputLogicException.ConcurrencyTokenRequired);
        if (!TryParseState(request.State, out var state))
            throw new InputLogicException(Resources.InputLogicException.StateInvalid);

        VersionMutationResult result;
        try
        {
            result = await sender.Send(new ChangeVersionStateCommand(objectId, version, state,
                request.ExpectedConcurrencyToken), cancellationToken);
        }
        catch (VersionWriteUncertainException)
        {
            return UncertainWrite();
        }

        var response = Map(result);
        return response ?? Ok(ToDto(result));
    }

    private IActionResult? Map(VersionMutationResult result) => result.Status switch
    {
        VersionMutationStatus.Succeeded => null,
        VersionMutationStatus.NotFound => NotFound(ToErrorDto(result.Error)),
        VersionMutationStatus.Conflict => Conflict(ToErrorDto(result.Error)),
        VersionMutationStatus.Invalid => BadRequest(ToErrorDto(result.Error)),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };

    private static VersionMutationDto ToDto(VersionMutationResult result) => new()
    {
        ObjectId = result.ObjectId,
        VersionId = result.VersionId!.Value,
        VersionNumber = result.VersionNumber!.Value,
        State = result.State!.Value.ToString(),
        CurrentVersionId = result.CurrentVersionId,
        ConcurrencyToken = result.ConcurrencyToken!.Value,
        Warnings = result.Warnings
    };

    private static VersionMutationErrorDto ToErrorDto(VersionMutationError? error) => error is null
        ? new()
        {
            Code = "VersionMutationFailed",
            Message = Resources.BusinessLogicException.VersionMutationFailed,
            CyclePath = null
        } : new()
        {
            Code = error.Code,
            Message = error.Message,
            CyclePath = error.CyclePath
        };

    private ObjectResult UncertainWrite() => Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "The version write outcome is unknown.",
        detail: "Read the object card to determine whether the change was committed before retrying.");

    private static bool TryParseState(string? value, out VersionState state)
    {
        state = default;
        return value is not null && Enum.GetNames<VersionState>().Any(name =>
            string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) &&
            Enum.TryParse(value, ignoreCase: true, out state) && Enum.IsDefined(state);
    }
}
