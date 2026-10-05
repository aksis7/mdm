using MiniPdm.Common.Exceptions;
using Resources = MiniPdm.Common.Resources;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MiniPdm.Contracts.Modules.Calculations.DtoModels;
using MiniPdm.Modules.Calculations.Features.Queries.GetCompositionCalculation;

namespace MiniPdm.Modules.Calculations.Controllers;

/// <summary>
/// Предоставляет HTTP-операцию расчёта массы и спецификации объекта.
/// Расчёт выполняется обработчиком запроса модуля вычислений.
/// </summary>
/// <param name="sender">Посредник для отправки запроса расчёта.</param>
[ApiController]
[Route("api/objects/{objectId:guid}/calculations")]
public sealed class CalculationsController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Рассчитывает состав и массу указанного объекта.
    /// </summary>
    /// <param name="objectId">Идентификатор корневого объекта.</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса.</param>
    /// <returns>Расчёт объекта, ошибка запроса или статус отсутствующего объекта.</returns>
    [HttpGet]
    [ProducesResponseType<CompositionCalculationDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CompositionCalculationDto>> Get(Guid objectId, CancellationToken cancellationToken)
    {
        if (objectId == Guid.Empty)
            throw new InputLogicException(Resources.InputLogicException.ObjectIdRequired);

        var result = await sender.Send(new GetCompositionCalculationQuery(objectId), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
