using MediatR;
using MiniPdm.Domain.Versions.Mutations;
using MiniPdm.Modules.Versions.Abstractions;

namespace MiniPdm.Modules.Objects.Features.Commands.UpdateVersionAttributes;

/// <summary>
/// Обновляет изменяемые атрибуты версии объекта.
/// Изменение выполняется только при совпадении ожидаемого токена конкурентности.
/// </summary>
public sealed record UpdateVersionAttributesCommand(Guid ObjectId, int Version, string? Name, string? Material,
    decimal? Mass, Guid ExpectedConcurrencyToken) : IRequest<VersionMutationResult>
{
    /// <summary>
    /// Идентификатор объекта, версию которого обновляют.
    /// </summary>
    public Guid ObjectId { get; init; } = ObjectId;

    /// <summary>
    /// Положительный номер изменяемой версии.
    /// </summary>
    public int Version { get; init; } = Version;

    /// <summary>
    /// Новое наименование версии, если оно задаётся.
    /// </summary>
    public string? Name { get; init; } = Name;

    /// <summary>
    /// Новый материал версии, если он задаётся.
    /// </summary>
    public string? Material { get; init; } = Material;

    /// <summary>
    /// Новая масса версии, если она задаётся.
    /// </summary>
    public decimal? Mass { get; init; } = Mass;

    /// <summary>
    /// Токен объекта, по которому проверяется актуальность изменения.
    /// </summary>
    public Guid ExpectedConcurrencyToken { get; init; } = ExpectedConcurrencyToken;
}

/// <summary>
/// Передаёт обновление атрибутов прикладному сервису мутаций версии.
/// </summary>
/// <param name="service">Сервис проверки и сохранения атрибутов.</param>
public sealed class UpdateVersionAttributesCommandHandler(IVersionMutationService service)
    : IRequestHandler<UpdateVersionAttributesCommand, VersionMutationResult>
{
    /// <summary>
    /// Обновляет атрибуты выбранной версии.
    /// </summary>
    /// <param name="request">Команда с новыми значениями и токеном конкурентности.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Итог мутации с новым состоянием или диагностикой отказа.</returns>
    public Task<VersionMutationResult> Handle(UpdateVersionAttributesCommand request, CancellationToken cancellationToken) =>
        service.UpdateAttributesAsync(request.ObjectId, request.Version, request.Name, request.Material, request.Mass,
            request.ExpectedConcurrencyToken, cancellationToken);
}
