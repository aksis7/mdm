using MiniPdm.Common.Exceptions;
using Resources = MiniPdm.Common.Resources;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MiniPdm.Contracts.Modules.Objects.DtoModels;
using MiniPdm.Contracts.Modules.Versions.DtoModels;
using MiniPdm.Domain.Versions.Mutations;
using MiniPdm.Modules.Objects.Features.Queries.GetObject;
using MiniPdm.Modules.Objects.Features.Queries.SearchObjects;
using MiniPdm.Modules.Objects.Features.Commands.UpdateVersionAttributes;
using MiniPdm.Modules.Versions.Services;

namespace MiniPdm.Modules.Objects.Controllers;

/// <summary>
/// Предоставляет HTTP-операции поиска объектов, чтения карточек и редактирования версий.
/// Запросы и команды выполняются через MediatR.
/// </summary>
/// <param name="sender">Посредник для отправки запросов и команд.</param>
[ApiController]
[Route("api/objects")]
public sealed class ObjectsController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Ищет объекты по строке и возвращает страницу результатов.
    /// </summary>
    /// <param name="search">Подстрока поиска; при отсутствии используется пустая строка.</param>
    /// <param name="offset">Смещение страницы от начала результатов.</param>
    /// <param name="limit">Число результатов на странице от 1 до 100.</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса.</param>
    /// <returns>Страница объектов или ошибка при недопустимых параметрах.</returns>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? search = null, [FromQuery] int offset = 0, [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        search ??= string.Empty;
        if (search.Length > 512)
            throw new InputLogicException(Resources.InputLogicException.SearchTooLong);
        if (offset < 0)
            throw new InputLogicException(Resources.InputLogicException.OffsetNonnegative);
        if (limit is < 1 or > 100)
            throw new InputLogicException(Resources.InputLogicException.LimitRange);

        return Ok(await sender.Send(new SearchObjectsQuery(search, offset, limit), cancellationToken));
    }

    /// <summary>
    /// Возвращает карточку объекта и при необходимости историческую версию.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта.</param>
    /// <param name="version">Номер версии для просмотра или <see langword="null"/> для текущей версии.</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса.</param>
    /// <returns>Карточка объекта, ошибка запроса или статус отсутствующего объекта/версии.</returns>
    [HttpGet("{objectId:guid}")]
    public async Task<IActionResult> Get(Guid objectId, [FromQuery] int? version = null, CancellationToken cancellationToken = default)
    {
        if (objectId == Guid.Empty)
            throw new InputLogicException(Resources.InputLogicException.ObjectIdRequired);
        if (version is <= 0)
            throw new InputLogicException(Resources.InputLogicException.VersionMustBePositive);

        var result = await sender.Send(new GetObjectQuery(objectId, version), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Обновляет атрибуты выбранной версии объекта.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта.</param>
    /// <param name="version">Положительный номер изменяемой версии.</param>
    /// <param name="request">Тело с новыми атрибутами и ожидаемым токеном конкурентности.</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса.</param>
    /// <returns>Результат мутации или соответствующий HTTP-ответ при ошибке, конфликте либо неизвестном результате записи.</returns>
    [HttpPut("{objectId:guid}/versions/{version:int}/attributes")]
    public async Task<IActionResult> UpdateVersionAttributes(Guid objectId, int version,
        [FromBody] UpdateVersionAttributesRequestDto? request, CancellationToken cancellationToken = default)
    {
        if (objectId == Guid.Empty)
            throw new InputLogicException(Resources.InputLogicException.ObjectIdRequired);
        if (version <= 0)
            throw new InputLogicException(Resources.InputLogicException.VersionMustBePositive);
        if (request is null)
            throw new InputLogicException(Resources.InputLogicException.RequestBodyRequired);
        if (request.ExpectedConcurrencyToken == Guid.Empty)
            throw new InputLogicException(Resources.InputLogicException.ConcurrencyTokenRequired);

        VersionMutationResult result;
        try
        {
            result = await sender.Send(new UpdateVersionAttributesCommand(objectId, version, request.Name,
                request.Material, request.Mass, request.ExpectedConcurrencyToken), cancellationToken);
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
}
