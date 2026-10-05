using MediatR;
using MiniPdm.Contracts.Modules.Composition.DtoModels;
using MiniPdm.Modules.Composition.Services;

namespace MiniPdm.Modules.Composition.Features.Queries.GetComposition;

/// <summary>
/// Запрашивает дерево состава, начинающееся с указанного объекта.
/// В ответ входят вложенные вхождения и сведения об их текущих версиях.
/// </summary>
public sealed record GetCompositionQuery(Guid ObjectId) : IRequest<CompositionTreeDto?>
{
    /// <summary>
    /// Идентификатор корневого объекта.
    /// </summary>
    public Guid ObjectId { get; init; } = ObjectId;
}

/// <summary>
/// Получает дерево состава объекта через сервис чтения состава.
/// </summary>
/// <param name="service">Сервис построения дерева состава.</param>
public sealed class GetCompositionQueryHandler(CompositionReadService service)
    : IRequestHandler<GetCompositionQuery, CompositionTreeDto?>
{
    /// <summary>
    /// Выполняет запрос дерева состава.
    /// </summary>
    /// <param name="request">Запрос с идентификатором корневого объекта.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Дерево состава или <see langword="null"/>, если объект не найден.</returns>
    public Task<CompositionTreeDto?> Handle(GetCompositionQuery request, CancellationToken cancellationToken) =>
        service.GetCompositionAsync(request.ObjectId, cancellationToken);
}
