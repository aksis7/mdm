namespace MiniPdm.Contracts.Modules.Calculations.DtoModels;

/// <summary>
/// Содержит рассчитанную массу сборки, спецификацию и диагностику.
/// Массы выражаются в килограммах, количества — в штуках.
/// </summary>
public sealed record CompositionCalculationDto
{
    /// <summary>
    /// Идентификатор корневого объекта расчёта.
    /// </summary>
    public Guid RootObjectId
    {
        get; init;
    }

    /// <summary>
    /// Суммарная масса сборки в килограммах либо <see langword="null"/>, если её нельзя определить.
    /// </summary>
    public decimal? TotalMassKg
    {
        get; init;
    }

    /// <summary>
    /// Показывает, известна ли полная суммарная масса с учётом диагностики.
    /// </summary>
    public bool IsComplete
    {
        get; init;
    }

    /// <summary>
    /// Строки плоской спецификации.
    /// </summary>
    public IReadOnlyList<SpecificationItemDto> Items { get; init; } = default!;

    /// <summary>
    /// Диагностические сообщения, возникшие при расчёте.
    /// </summary>
    public IReadOnlyList<CalculationDiagnosticDto> Diagnostics { get; init; } = default!;

}

/// <summary>
/// Строка плоской спецификации с количеством и массами.
/// Количества указаны в целых штуках; неизвестные значения представлены как <see langword="null"/>.
/// </summary>
public sealed record SpecificationItemDto
{
    /// <summary>
    /// Идентификатор объекта спецификации.
    /// </summary>
    public Guid ObjectId
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
    /// Материал объекта либо <see langword="null"/>.
    /// </summary>
    public string? Material
    {
        get; init;
    }

    /// <summary>
    /// Идентификатор учтённой версии либо <see langword="null"/>.
    /// </summary>
    public Guid? VersionId
    {
        get; init;
    }

    /// <summary>
    /// Номер учтённой версии либо <see langword="null"/>.
    /// </summary>
    public int? VersionNumber
    {
        get; init;
    }

    /// <summary>
    /// Количество объекта в спецификации либо <see langword="null"/>.
    /// </summary>
    public decimal? Quantity
    {
        get; init;
    }

    /// <summary>
    /// Масса одной единицы в килограммах либо <see langword="null"/>.
    /// </summary>
    public decimal? UnitMassKg
    {
        get; init;
    }

    /// <summary>
    /// Суммарная масса данного объекта в килограммах либо <see langword="null"/>.
    /// </summary>
    public decimal? TotalMassKg
    {
        get; init;
    }
}

/// <summary>
/// Диагностика, обнаруженная во время расчёта состава.
/// </summary>
public sealed record CalculationDiagnosticDto
{
    /// <summary>
    /// Машиночитаемый код диагностики.
    /// </summary>
    public string Code { get; init; } = default!;

    /// <summary>
    /// Идентификатор объекта, к которому относится диагностика.
    /// </summary>
    public Guid ObjectId
    {
        get; init;
    }

    /// <summary>
    /// Путь идентификаторов от корневого объекта до проблемного объекта.
    /// </summary>
    public Guid[] ObjectPath { get; init; } = default!;

    /// <summary>
    /// Описание обнаруженной проблемы.
    /// </summary>
    public string Message { get; init; } = default!;

}
