using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;

namespace MiniPdm.Modules.Composition.DtoModels;

/// <summary>
/// Одно вхождение объекта на пути дерева состава.
/// Данные включают путь, локальное количество и текущую версию объекта.
/// </summary>
public sealed record CompositionOccurrenceDto
{
    /// <summary>
    /// Идентификатор объекта, представленного этим вхождением дерева.
    /// </summary>
    public Guid ObjectId
    {
        get; init;
    }

    /// <summary>
    /// Последовательность идентификаторов от корня дерева до данного объекта включительно.
    /// </summary>
    public Guid[] ObjectPath { get; init; } = default!;

    /// <summary>
    /// Последовательность идентификаторов от корня до родителя объекта.
    /// Для корневого вхождения путь родителя отсутствует.
    /// </summary>
    public Guid[]? ParentPath
    {
        get; init;
    }

    /// <summary>
    /// Количество объекта относительно его непосредственного родителя.
    /// Для корневого вхождения значение равно единице.
    /// </summary>
    public int LocalQuantity
    {
        get; init;
    }

    /// <summary>
    /// Тип объекта PDM, определяющий его свойства и правила расчёта.
    /// </summary>
    public PdmObjectType Type
    {
        get; init;
    }

    /// <summary>
    /// Обозначение сборки или детали; для стандартного изделия не задаётся.
    /// </summary>
    public string? Designation
    {
        get; init;
    }

    /// <summary>
    /// Отображаемое имя объекта или имя его текущей версии.
    /// </summary>
    public string? Name
    {
        get; init;
    }

    /// <summary>
    /// Материал текущей версии, если он задан для объекта.
    /// </summary>
    public string? Material
    {
        get; init;
    }

    /// <summary>
    /// Идентификатор текущей версии объекта; равен null при её отсутствии.
    /// </summary>
    public Guid? VersionId
    {
        get; init;
    }

    /// <summary>
    /// Номер текущей версии объекта; равен null при её отсутствии.
    /// </summary>
    public int? VersionNumber
    {
        get; init;
    }

    /// <summary>
    /// Состояние текущей версии объекта, если она существует.
    /// </summary>
    public VersionState? State
    {
        get; init;
    }

    /// <summary>
    /// Масса одной единицы объекта в килограммах, если она известна.
    /// Масса сборки в этой проекции не вычисляется.
    /// </summary>
    public decimal? UnitMassKg
    {
        get; init;
    }

    /// <summary>
    /// Показывает, что объект уже присутствует в пути и образует цикл.
    /// </summary>
    public bool IsCycle
    {
        get; init;
    }
}
