using MediatR;
using MiniPdm.Contracts.Modules.Calculations.DtoModels;
using MiniPdm.Modules.Calculations.Services;

namespace MiniPdm.Modules.Calculations.Features.Queries.GetCompositionCalculation;

/// <summary>
/// Запрашивает расчёт массы и плоскую спецификацию корневого объекта.
/// Результат содержит полноту расчёта и диагностику незавершённых ветвей.
/// </summary>
public sealed record GetCompositionCalculationQuery(Guid ObjectId) : IRequest<CompositionCalculationDto?>
{
    /// <summary>
    /// Идентификатор корневого объекта для расчёта.
    /// </summary>
    public Guid ObjectId { get; init; } = ObjectId;
}

/// <summary>
/// Выполняет запрос расчёта через сервис расчётов состава.
/// </summary>
/// <param name="service">Сервис расчёта состава и массы.</param>
public sealed class GetCompositionCalculationQueryHandler(CompositionCalculationService service)
    : IRequestHandler<GetCompositionCalculationQuery, CompositionCalculationDto?>
{
    /// <summary>
    /// Строит расчёт для корневого объекта.
    /// </summary>
    /// <param name="request">Запрос с идентификатором корневого объекта.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Расчёт состава или <see langword="null"/>, если объект отсутствует.</returns>
    public Task<CompositionCalculationDto?> Handle(GetCompositionCalculationQuery request,
        CancellationToken cancellationToken) => service.GetCalculationAsync(request.ObjectId, cancellationToken);
}
