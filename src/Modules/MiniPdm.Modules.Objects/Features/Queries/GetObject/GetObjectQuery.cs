using MediatR;
using MiniPdm.Contracts.Modules.Objects.DtoModels;
using MiniPdm.Modules.Objects.Services;

namespace MiniPdm.Modules.Objects.Features.Queries.GetObject;

/// <summary>
/// Запрашивает карточку объекта с его текущей или выбранной исторической версией.
/// </summary>
public sealed record GetObjectQuery(Guid ObjectId, int? VersionNumber) : IRequest<ObjectCardDto?>
{
    /// <summary>
    /// Идентификатор объекта для чтения.
    /// </summary>
    public Guid ObjectId { get; init; } = ObjectId;

    /// <summary>
    /// Номер версии для чтения или <see langword="null"/> для текущей версии.
    /// </summary>
    public int? VersionNumber { get; init; } = VersionNumber;
}

/// <summary>
/// Выполняет запрос карточки объекта через службу чтения.
/// </summary>
/// <param name="service">Служба чтения объектов и версий.</param>
public sealed class GetObjectQueryHandler(ObjectReadService service) : IRequestHandler<GetObjectQuery, ObjectCardDto?>
{
    /// <summary>
    /// Возвращает карточку указанного объекта.
    /// </summary>
    /// <param name="request">Запрос с идентификатором объекта и необязательным номером версии.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Карточка объекта либо <see langword="null"/>, если объект или версия отсутствуют.</returns>
    public Task<ObjectCardDto?> Handle(GetObjectQuery request, CancellationToken cancellationToken) =>
        service.GetObjectAsync(request.ObjectId, request.VersionNumber, cancellationToken);
}
