using MiniPdm.Domain.Objects;

namespace MiniPdm.Modules.Composition.DtoModels;

/// <summary>
/// Внутренняя проекция состава конкретной версии.
/// Содержит токен конкурентности и дочерние элементы состава.
/// </summary>
public sealed record VersionCompositionReadRowDto
{
    /// <summary>
    /// Идентификатор родительского объекта.
    /// </summary>
    public Guid ObjectId
    {
        get; init;
    }

    /// <summary>
    /// Номер версии, состав которой прочитан.
    /// </summary>
    public int Version
    {
        get; init;
    }

    /// <summary>
    /// Токен объекта для последующего изменения состава.
    /// </summary>
    public Guid ConcurrencyToken
    {
        get; init;
    }

    /// <summary>
    /// Дочерние объекты и сведения об их текущих версиях.
    /// </summary>
    public IReadOnlyList<VersionCompositionItemReadRowDto> Items { get; init; } = default!;
}

/// <summary>
/// Одна строка внутренней проекции состава версии.
/// Имя и обозначение описывают дочерний объект, а не конкретную версию.
/// </summary>
public sealed record VersionCompositionItemReadRowDto
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
    /// Тип дочернего объекта PDM.
    /// </summary>
    public PdmObjectType Type
    {
        get; init;
    }

    /// <summary>
    /// Обозначение дочерней сборки или детали.
    /// </summary>
    public string? Designation
    {
        get; init;
    }

    /// <summary>
    /// Имя дочернего объекта или его текущей версии.
    /// </summary>
    public string? Name
    {
        get; init;
    }

    /// <summary>
    /// Показывает, что у дочернего объекта нет текущей версии.
    /// </summary>
    public bool NoCurrentVersion
    {
        get; init;
    }
}
