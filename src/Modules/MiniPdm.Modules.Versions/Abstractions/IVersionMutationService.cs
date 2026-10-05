using MiniPdm.Domain.Composition;
using MiniPdm.Domain.Versions;
using MiniPdm.Domain.Versions.Mutations;

namespace MiniPdm.Modules.Versions.Abstractions;

/// <summary>
/// Выполняет проверенные изменения версий объекта с контролем конкурентного доступа.
/// </summary>
public interface IVersionMutationService
{
    /// <summary>
    /// Создаёт новую версию по содержимому существующей.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта.</param>
    /// <param name="sourceVersion">Номер исходной версии.</param>
    /// <param name="expectedConcurrencyToken">Ожидаемый токен конкурентного изменения.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Результат создания версии, включая статус и возможную ошибку проверки.</returns>
    Task<VersionMutationResult> CloneAsync(Guid objectId, int sourceVersion,
        Guid expectedConcurrencyToken, CancellationToken ct);

    /// <summary>
    /// Меняет состояние заданной версии.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта.</param>
    /// <param name="version">Номер версии.</param>
    /// <param name="state">Новое состояние.</param>
    /// <param name="expectedConcurrencyToken">Ожидаемый токен конкурентного изменения.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Результат изменения состояния.</returns>
    Task<VersionMutationResult> ChangeStateAsync(Guid objectId, int version, VersionState state,
        Guid expectedConcurrencyToken, CancellationToken ct);

    /// <summary>
    /// Изменяет атрибуты заданной версии.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта.</param>
    /// <param name="version">Номер версии.</param>
    /// <param name="name">Новое наименование либо <see langword="null"/>.</param>
    /// <param name="material">Новый материал либо <see langword="null"/>.</param>
    /// <param name="mass">Новая масса либо <see langword="null"/>.</param>
    /// <param name="expectedConcurrencyToken">Ожидаемый токен конкурентного изменения.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Результат обновления атрибутов.</returns>
    Task<VersionMutationResult> UpdateAttributesAsync(Guid objectId, int version, string? name,
        string? material, decimal? mass, Guid expectedConcurrencyToken, CancellationToken ct);

    /// <summary>
    /// Заменяет состав заданной версии.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта.</param>
    /// <param name="version">Номер версии.</param>
    /// <param name="components">Новый состав с количествами компонентов.</param>
    /// <param name="expectedConcurrencyToken">Ожидаемый токен конкурентного изменения.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Результат замены состава, включая возможную ошибку правил графа.</returns>
    Task<VersionMutationResult> ReplaceCompositionAsync(Guid objectId, int version,
        IReadOnlyList<CompositionItem> components, Guid expectedConcurrencyToken, CancellationToken ct);
}
