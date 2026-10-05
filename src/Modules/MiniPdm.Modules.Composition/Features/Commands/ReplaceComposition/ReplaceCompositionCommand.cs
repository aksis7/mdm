using MediatR;
using MiniPdm.Domain.Versions.Mutations;
using MiniPdm.Modules.Versions.Abstractions;

namespace MiniPdm.Modules.Composition.Features.Commands.ReplaceComposition;

/// <summary>
/// Задаёт новый состав выбранной версии объекта.
/// Команда содержит ожидаемый токен конкурентности для защиты от устаревшего изменения.
/// </summary>
public sealed record ReplaceCompositionCommand(Guid ObjectId, int Version, IReadOnlyList<CompositionItem> Components,
    Guid ExpectedConcurrencyToken) : IRequest<VersionMutationResult>
{
    /// <summary>
    /// Идентификатор объекта, состав версии которого заменяется.
    /// </summary>
    public Guid ObjectId { get; init; } = ObjectId;

    /// <summary>
    /// Положительный номер версии объекта.
    /// </summary>
    public int Version { get; init; } = Version;

    /// <summary>
    /// Новый состав в виде дочерних объектов и их количеств.
    /// </summary>
    public IReadOnlyList<CompositionItem> Components { get; init; } = Components;

    /// <summary>
    /// Токен объекта, прочитанный клиентом перед изменением.
    /// </summary>
    public Guid ExpectedConcurrencyToken { get; init; } = ExpectedConcurrencyToken;
}

/// <summary>
/// Передаёт команду замены состава прикладному сервису мутаций версии.
/// </summary>
/// <param name="service">Сервис, выполняющий проверку и сохранение изменения.</param>
public sealed class ReplaceCompositionCommandHandler(IVersionMutationService service)
    : IRequestHandler<ReplaceCompositionCommand, VersionMutationResult>
{
    /// <summary>
    /// Выполняет замену состава версии с учётом токена конкурентности.
    /// </summary>
    /// <param name="request">Команда с объектом, версией и новым составом.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Итог мутации, включая состояние версии и диагностические сведения.</returns>
    public Task<VersionMutationResult> Handle(ReplaceCompositionCommand request, CancellationToken cancellationToken) =>
        service.ReplaceCompositionAsync(request.ObjectId, request.Version, request.Components,
            request.ExpectedConcurrencyToken, cancellationToken);
}
