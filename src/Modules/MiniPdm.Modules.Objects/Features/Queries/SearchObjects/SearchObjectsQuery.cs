using MediatR;
using MiniPdm.Contracts.Modules.Objects.DtoModels;
using MiniPdm.Modules.Objects.Services;

namespace MiniPdm.Modules.Objects.Features.Queries.SearchObjects;

/// <summary>
/// Запрашивает страницу объектов, соответствующих строке поиска.
/// Параметры смещения и размера задают границы страницы.
/// </summary>
public sealed record SearchObjectsQuery(string Search, int Offset, int Limit) : IRequest<ObjectSearchPageDto>
{
    /// <summary>
    /// Строка поиска по обозначению и имени объекта.
    /// </summary>
    public string Search { get; init; } = Search;

    /// <summary>
    /// Число результатов, пропускаемых перед страницей.
    /// </summary>
    public int Offset { get; init; } = Offset;

    /// <summary>
    /// Максимальное число результатов страницы.
    /// </summary>
    public int Limit { get; init; } = Limit;
}

/// <summary>
/// Выполняет запрос поиска через службу чтения объектов.
/// </summary>
/// <param name="service">Служба поиска и чтения карточек объектов.</param>
public sealed class SearchObjectsQueryHandler(ObjectReadService service) : IRequestHandler<SearchObjectsQuery, ObjectSearchPageDto>
{
    /// <summary>
    /// Ищет объекты и возвращает сформированную страницу результатов.
    /// </summary>
    /// <param name="request">Строка поиска и параметры постраничной выдачи.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Публичная страница поиска объектов.</returns>
    public Task<ObjectSearchPageDto> Handle(SearchObjectsQuery request, CancellationToken cancellationToken) =>
        service.SearchObjectsAsync(request.Search, request.Offset, request.Limit, cancellationToken);
}
