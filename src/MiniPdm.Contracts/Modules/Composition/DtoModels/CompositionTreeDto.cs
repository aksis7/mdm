namespace MiniPdm.Contracts.Modules.Composition.DtoModels;

/// <summary>
/// Представляет плоский список вхождений состава объекта.
/// Пути сохраняют структуру дерева без рекурсивного JSON-вложенного представления.
/// </summary>
public sealed record CompositionTreeDto
{
    /// <summary>
    /// Идентификатор корневого объекта состава.
    /// </summary>
    public Guid RootObjectId
    {
        get; init;
    }

    /// <summary>
    /// Узлы состава в порядке обхода дерева.
    /// </summary>
    public IReadOnlyList<CompositionNodeDto> Nodes { get; init; } = default!;

}

/// <summary>
/// Описывает одно вхождение объекта в дереве состава и его путь от корня.
/// </summary>
public sealed record CompositionNodeDto
{
    /// <summary>
    /// Идентификатор объекта в этом вхождении.
    /// </summary>
    public Guid ObjectId
    {
        get; init;
    }

    /// <summary>
    /// Путь идентификаторов от корневого объекта до данного вхождения.
    /// </summary>
    public IReadOnlyList<Guid> ObjectPath { get; init; } = default!;

    /// <summary>
    /// Путь родителя либо <see langword="null"/> для корневого узла.
    /// </summary>
    public IReadOnlyList<Guid>? ParentPath
    {
        get; init;
    }

    /// <summary>
    /// Количество объекта в непосредственном родительском узле.
    /// </summary>
    public int LocalQuantity
    {
        get; init;
    }

    /// <summary>
    /// Тип объекта.
    /// </summary>
    public string Type { get; init; } = default!;

    /// <summary>
    /// Обозначение объекта либо <see langword="null"/>.
    /// </summary>
    public string? Designation
    {
        get; init;
    }

    /// <summary>
    /// Наименование объекта либо <see langword="null"/>.
    /// </summary>
    public string? Name
    {
        get; init;
    }

    /// <summary>
    /// Материал выбранной версии либо <see langword="null"/>.
    /// </summary>
    public string? Material
    {
        get; init;
    }

    /// <summary>
    /// Идентификатор использованной версии либо <see langword="null"/>.
    /// </summary>
    public Guid? VersionId
    {
        get; init;
    }

    /// <summary>
    /// Номер использованной версии либо <see langword="null"/>.
    /// </summary>
    public int? VersionNumber
    {
        get; init;
    }

    /// <summary>
    /// Состояние использованной версии либо <see langword="null"/>.
    /// </summary>
    public string? State
    {
        get; init;
    }

    /// <summary>
    /// Масса единицы в килограммах либо <see langword="null"/>.
    /// </summary>
    public decimal? UnitMassKg
    {
        get; init;
    }

    /// <summary>
    /// Код ошибки получения сведений об узле либо <see langword="null"/>.
    /// </summary>
    public string? ErrorCode
    {
        get; init;
    }

    /// <summary>
    /// Описание ошибки получения сведений об узле либо <see langword="null"/>.
    /// </summary>
    public string? Error
    {
        get; init;
    }
}
