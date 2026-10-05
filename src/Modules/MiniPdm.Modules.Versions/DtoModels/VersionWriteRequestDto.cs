namespace MiniPdm.Modules.Versions.DtoModels;

/// <summary>
/// Предусловие конкурентного доступа и данные для подготовки одной мутации версии.
/// </summary>
public sealed record VersionWriteRequestDto
{
    /// <summary>
    /// Идентификатор изменяемого объекта.
    /// </summary>
    public Guid ObjectId
    {
        get; init;
    }

    /// <summary>
    /// Номер версии, к которой относится мутация.
    /// </summary>
    public int VersionNumber
    {
        get; init;
    }

    /// <summary>
    /// Токен конкурентности, ожидаемый от клиента.
    /// </summary>
    public Guid ExpectedConcurrencyToken
    {
        get; init;
    }

    /// <summary>
    /// Идентификаторы дочерних объектов для предварительной проверки.
    /// </summary>
    public IReadOnlyCollection<Guid> ReferencedChildIds { get; init; } = default!;
}
