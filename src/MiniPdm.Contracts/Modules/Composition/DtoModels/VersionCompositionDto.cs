namespace MiniPdm.Contracts.Modules.Composition.DtoModels;

/// <summary>
/// Снимок непосредственного состава одной версии объекта.
/// Элементы ссылаются только на дочерние объекты и не раскрывают их состав.
/// </summary>
public sealed record VersionCompositionDto
{
    /// <summary>
    /// Идентификатор объекта-владельца состава.
    /// </summary>
    public Guid ObjectId
    {
        get; init;
    }

    /// <summary>
    /// Номер версии, для которой получен состав.
    /// </summary>
    public int Version
    {
        get; init;
    }

    /// <summary>
    /// Токен конкурентного доступа версии.
    /// </summary>
    public Guid ConcurrencyToken
    {
        get; init;
    }

    /// <summary>
    /// Непосредственные компоненты версии.
    /// </summary>
    public IReadOnlyList<VersionCompositionItemDto> Items { get; init; } = default!;

}

/// <summary>
/// Один непосредственный компонент состава версии.
/// </summary>
public sealed record VersionCompositionItemDto
{
    /// <summary>
    /// Идентификатор дочернего объекта.
    /// </summary>
    public Guid ChildObjectId
    {
        get; init;
    }

    /// <summary>
    /// Количество дочернего объекта в составе.
    /// </summary>
    public int Quantity
    {
        get; init;
    }

    /// <summary>
    /// Тип дочернего объекта.
    /// </summary>
    public string Type { get; init; } = default!;

    /// <summary>
    /// Обозначение дочернего объекта либо <see langword="null"/>.
    /// </summary>
    public string? Designation
    {
        get; init;
    }

    /// <summary>
    /// Наименование дочернего объекта либо <see langword="null"/>.
    /// </summary>
    public string? Name
    {
        get; init;
    }

    /// <summary>
    /// Указывает, что у дочернего объекта нет текущей версии.
    /// </summary>
    public bool NoCurrentVersion
    {
        get; init;
    }
}
