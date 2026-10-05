using Resources = MiniPdm.Common.Resources;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;

namespace MiniPdm.Domain.Calculations;

/// <summary>
///     Вхождение объекта в развёрнутом дереве с данными для расчёта массы и спецификации.
/// </summary>
public sealed record CompositionCalculationInput(
    Guid ObjectId,
    IReadOnlyList<Guid> ObjectPath,
    IReadOnlyList<Guid>? ParentPath,
    int LocalQuantity,
    PdmObjectType Type,
    string? Designation,
    string? Name,
    string? Material,
    Guid? VersionId,
    int? VersionNumber,
    VersionState? State,
    decimal? UnitMassKg,
    bool IsCycle)
{
    /// <summary>
    ///     Идентификатор объекта в этом вхождении.
    /// </summary>
    public Guid ObjectId { get; init; } = ObjectId;

    /// <summary>
    ///     Идентификаторы объектов от корня до этого вхождения.
    /// </summary>
    public IReadOnlyList<Guid> ObjectPath { get; init; } = ObjectPath;

    /// <summary>
    ///     Путь родительского вхождения; для корня равен null.
    /// </summary>
    public IReadOnlyList<Guid>? ParentPath { get; init; } = ParentPath;

    /// <summary>
    ///     Положительное целое количество экземпляров на связи с родительским вхождением.
    /// </summary>
    public int LocalQuantity { get; init; } = LocalQuantity;

    /// <summary>
    ///     Тип объекта PDM.
    /// </summary>
    public PdmObjectType Type { get; init; } = Type;

    /// <summary>
    ///     Обозначение детали или сборки, если оно применимо.
    /// </summary>
    public string? Designation { get; init; } = Designation;

    /// <summary>
    ///     Отображаемое наименование объекта, если оно задано.
    /// </summary>
    public string? Name { get; init; } = Name;

    /// <summary>
    ///     Материал текущей версии, если он применим.
    /// </summary>
    public string? Material { get; init; } = Material;

    /// <summary>
    ///     Идентификатор текущей версии; null, если действующей версии нет.
    /// </summary>
    public Guid? VersionId { get; init; } = VersionId;

    /// <summary>
    ///     Номер текущей версии, если он задан.
    /// </summary>
    public int? VersionNumber { get; init; } = VersionNumber;

    /// <summary>
    ///     Состояние текущей версии, если она задана.
    /// </summary>
    public VersionState? State { get; init; } = State;

    /// <summary>
    ///     Масса одного изделия в килограммах, если она известна.
    /// </summary>
    public decimal? UnitMassKg { get; init; } = UnitMassKg;

    /// <summary>
    ///     Замыкает ли это вхождение цикл состава.
    /// </summary>
    public bool IsCycle { get; init; } = IsCycle;
}

/// <summary>
///     Рассчитанная строка спецификации: количество указано в штуках, масса — в килограммах.
/// </summary>
public sealed record CalculatedSpecificationItem(
    Guid ObjectId,
    PdmObjectType Type,
    string? Designation,
    string? Name,
    string? Material,
    Guid? VersionId,
    int? VersionNumber,
    decimal? Quantity,
    decimal? UnitMassKg,
    decimal? TotalMassKg)
{
    /// <summary>
    ///     Идентификатор объекта спецификации.
    /// </summary>
    public Guid ObjectId { get; init; } = ObjectId;

    /// <summary>
    ///     Тип объекта PDM.
    /// </summary>
    public PdmObjectType Type { get; init; } = Type;

    /// <summary>
    ///     Обозначение детали или сборки, если оно применимо.
    /// </summary>
    public string? Designation { get; init; } = Designation;

    /// <summary>
    ///     Отображаемое наименование объекта, если оно задано.
    /// </summary>
    public string? Name { get; init; } = Name;

    /// <summary>
    ///     Материал текущей версии, если он применим.
    /// </summary>
    public string? Material { get; init; } = Material;

    /// <summary>
    ///     Идентификатор текущей версии; null, если действующей версии нет.
    /// </summary>
    public Guid? VersionId { get; init; } = VersionId;

    /// <summary>
    ///     Номер текущей версии, если он задан.
    /// </summary>
    public int? VersionNumber { get; init; } = VersionNumber;

    /// <summary>
    ///     Количество целых изделий, суммированное по всем путям; null означает, что хотя бы одно слагаемое неизвестно.
    /// </summary>
    public decimal? Quantity { get; init; } = Quantity;

    /// <summary>
    ///     Масса одного изделия в килограммах, если она известна.
    /// </summary>
    public decimal? UnitMassKg { get; init; } = UnitMassKg;

    /// <summary>
    ///     Количество, умноженное на массу одного изделия; null, если одно из значений неизвестно.
    /// </summary>
    public decimal? TotalMassKg { get; init; } = TotalMassKg;
}

/// <summary>
///     Описывает отсутствующее или неверное значение, из-за которого расчёт состава неполон.
/// </summary>
public sealed record CalculationDiagnostic(string Code, Guid ObjectId, IReadOnlyList<Guid> ObjectPath, string Message)
{
    /// <summary>
    ///     Стабильный код диагностики, например <c>MissingMass</c> или <c>Cycle</c>.
    /// </summary>
    public string Code { get; init; } = Code;

    /// <summary>
    ///     Идентификатор объекта, связанного с проблемой.
    /// </summary>
    public Guid ObjectId { get; init; } = ObjectId;

    /// <summary>
    ///     Путь от корневого объекта до проблемного вхождения.
    /// </summary>
    public IReadOnlyList<Guid> ObjectPath { get; init; } = ObjectPath;

    /// <summary>
    ///     Понятное пользователю описание проблемы.
    /// </summary>
    public string Message { get; init; } = Message;
}

/// <summary>
///     Результат расчёта спецификации и массы. При наличии диагностики общая масса считается неизвестной.
/// </summary>
public sealed record CompositionCalculationResult(
    Guid RootObjectId,
    decimal? TotalMassKg,
    bool IsComplete,
    IReadOnlyList<CalculatedSpecificationItem> Items,
    IReadOnlyList<CalculationDiagnostic> Diagnostics)
{
    /// <summary>
    ///     Идентификатор корневого объекта расчёта.
    /// </summary>
    public Guid RootObjectId { get; init; } = RootObjectId;

    /// <summary>
    ///     Рассчитанная общая масса; null, если возникла хотя бы одна диагностика.
    /// </summary>
    public decimal? TotalMassKg { get; init; } = TotalMassKg;

    /// <summary>
    ///     Равно true только при отсутствии диагностик расчёта.
    /// </summary>
    public bool IsComplete { get; init; } = IsComplete;

    /// <summary>
    ///     Плоские строки спецификации, сгруппированные по объекту.
    /// </summary>
    public IReadOnlyList<CalculatedSpecificationItem> Items { get; init; } = Items;

    /// <summary>
    ///     Проблемы, обнаруженные при расчёте дерева объектов.
    /// </summary>
    public IReadOnlyList<CalculationDiagnostic> Diagnostics { get; init; } = Diagnostics;
}

/// <summary>
///     Рассчитывает количества в составе, массы деталей и плоскую спецификацию без обращения к хранилищу.
/// </summary>
public static class CompositionCalculator
{
    /// <summary>
    ///     Рассчитывает массу корневого объекта и плоскую спецификацию по развёрнутым вхождениям дерева.
    /// </summary>
    /// <param name="rootObjectId">
    ///     Идентификатор объекта, состав которого рассчитывается.
    /// </param>
    /// <param name="occurrences">
    ///     Вхождения из развёрнутого дерева состава.
    /// </param>
    /// <returns>
    ///     Рассчитанная спецификация, общая масса и найденные диагностики.
    /// </returns>
    public static CompositionCalculationResult Calculate(Guid rootObjectId, IEnumerable<CompositionCalculationInput> occurrences)
    {
        ArgumentNullException.ThrowIfNull(occurrences);

        var diagnostics = new List<CalculationDiagnostic>();
        var quantityByPath = new Dictionary<string, decimal?>(StringComparer.Ordinal);
        var groups = new Dictionary<Guid, SpecificationAccumulator>();
        var ordered = occurrences.OrderBy(x => x.ObjectPath.Count)
            .ThenBy(x => PathKey(x.ObjectPath), StringComparer.Ordinal).ToArray();

        foreach (var occurrence in ordered)
        {
            var path = occurrence.ObjectPath;
            var pathKey = PathKey(path);
            if (occurrence.IsCycle)
            {
                diagnostics.Add(Diagnostic("Cycle", occurrence,
                    Resources.BusinessLogicException.CompositionPathRepeated));
                continue;
            }

            decimal? quantity;
            if (path.Count == 1)
            {
                quantity = 1m;
            }
            else if (occurrence.ParentPath is null || !quantityByPath.TryGetValue(PathKey(occurrence.ParentPath), out var parentQuantity))
            {
                throw new ArgumentException("Every non-root occurrence must refer to an earlier parent path.", nameof(occurrences));
            }
            else if (parentQuantity is null)
            {
                // A failed ancestor already explains why this quantity is unknown.
                quantity = null;
            }
            else
            {
                try
                {
                    quantity = checked(parentQuantity.Value * occurrence.LocalQuantity);
                }
                catch (OverflowException)
                {
                    quantity = null;
                    diagnostics.Add(Diagnostic("QuantityOverflow", occurrence,
                        Resources.BusinessLogicException.QuantityOverflow));
                }
            }
            quantityByPath[pathKey] = quantity;

            if (occurrence.VersionId is null)
            {
                diagnostics.Add(Diagnostic("NoCurrentVersion", occurrence,
                    Resources.BusinessLogicException.NoCurrentVersion));
                if (occurrence.Type != PdmObjectType.Assembly)
                    AddOccurrence(groups, occurrence, quantity, diagnostics);
                continue;
            }

            if (occurrence.Type == PdmObjectType.Assembly)
                continue;

            if (occurrence.UnitMassKg is null)
            {
                diagnostics.Add(Diagnostic("MissingMass", occurrence,
                    Resources.BusinessLogicException.MissingMass));
            }

            AddOccurrence(groups, occurrence, quantity, diagnostics);
        }

        var items = new List<CalculatedSpecificationItem>(groups.Count);
        decimal totalMass = 0m;
        var totalOverflow = false;
        foreach (var group in groups.Values.OrderBy(x => PathKey(x.First.ObjectPath), StringComparer.Ordinal))
        {
            decimal? lineMass = null;
            if (group.Quantity is { } quantity && group.First.VersionId is not null && group.First.UnitMassKg is { } unitMass)
            {
                try
                {
                    lineMass = checked(quantity * unitMass);
                }
                catch (OverflowException)
                {
                    diagnostics.Add(Diagnostic("MassOverflow", group.First,
                        Resources.BusinessLogicException.SpecificationMassOverflow));
                }
            }

            items.Add(new CalculatedSpecificationItem(group.First.ObjectId, group.First.Type,
                group.First.Designation, group.First.Name, group.First.Material,
                group.First.VersionId, group.First.VersionNumber, group.Quantity,
                group.First.VersionId is null ? null : group.First.UnitMassKg, lineMass));

            if (lineMass is { } knownLineMass && !totalOverflow)
            {
                try
                {
                    totalMass = checked(totalMass + knownLineMass);
                }
                catch (OverflowException)
                {
                    diagnostics.Add(Diagnostic("MassOverflow", group.First,
                        Resources.BusinessLogicException.AssemblyMassOverflow));
                    totalOverflow = true;
                }
            }
        }

        var complete = diagnostics.Count == 0;
        return new CompositionCalculationResult(rootObjectId, complete ? totalMass : null,
            complete, items, diagnostics);
    }

    private static void AddOccurrence(Dictionary<Guid, SpecificationAccumulator> groups,
        CompositionCalculationInput occurrence, decimal? quantity, List<CalculationDiagnostic> diagnostics)
    {
        if (!groups.TryGetValue(occurrence.ObjectId, out var group))
        {
            groups.Add(occurrence.ObjectId, new SpecificationAccumulator(occurrence, quantity));
            return;
        }

        if (group.Quantity is null || quantity is null)
        {
            group.Quantity = null;
            return;
        }

        try
        {
            group.Quantity = checked(group.Quantity.Value + quantity.Value);
        }
        catch (OverflowException)
        {
            group.Quantity = null;
            diagnostics.Add(Diagnostic("QuantityOverflow", occurrence,
                Resources.BusinessLogicException.SpecificationQuantityOverflow));
        }
    }

    private static CalculationDiagnostic Diagnostic(string code, CompositionCalculationInput occurrence, string message) =>
        new(code, occurrence.ObjectId, occurrence.ObjectPath.ToArray(), message);

    private static string PathKey(IReadOnlyList<Guid> path) => string.Join("/", path.Select(x => x.ToString("N")));

    private sealed class SpecificationAccumulator(CompositionCalculationInput first, decimal? quantity)
    {
        /// <summary>
        ///     Первое вхождение объекта, задающее остальные данные строки спецификации.
        /// </summary>
        public CompositionCalculationInput First { get; } = first;

        /// <summary>
        ///     Суммарное количество по путям; null означает, что точное количество неизвестно.
        /// </summary>
        public decimal? Quantity { get; set; } = quantity;
    }
}
