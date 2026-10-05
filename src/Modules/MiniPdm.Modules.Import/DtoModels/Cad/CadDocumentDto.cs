using MiniPdm.Domain.Objects;

namespace MiniPdm.Modules.Import.DtoModels.Cad;

/// <summary>
/// Нормализованные сведения о CAD-документе и его составе.
/// </summary>
public sealed record CadDocumentDto
{
    /// <summary>
    /// Имя исходного CAD-файла.
    /// </summary>
    public string FileName { get; init; } = default!;

    /// <summary>
    /// Вид PDM-объекта, создаваемого из документа.
    /// </summary>
    public PdmObjectType Type
    {
        get; init;
    }

    /// <summary>
    /// Обозначение документа, если оно задано.
    /// </summary>
    public string? Designation
    {
        get; init;
    }

    /// <summary>
    /// Наименование объекта.
    /// </summary>
    public string Name { get; init; } = default!;

    /// <summary>
    /// Материал объекта, если он указан.
    /// </summary>
    public string? Material
    {
        get; init;
    }

    /// <summary>
    /// Масса объекта, если она указана.
    /// </summary>
    public decimal? Mass
    {
        get; init;
    }

    /// <summary>
    /// Ссылки на файлы компонентов и их количества.
    /// </summary>
    public IReadOnlyList<CadComponentDto> Components { get; init; } = default!;
}

/// <summary>
/// Позиция состава CAD-документа, ссылающаяся на другой файл пакета.
/// </summary>
public sealed record CadComponentDto
{
    /// <summary>
    /// Имя файла компонента.
    /// </summary>
    public string File { get; init; } = default!;

    /// <summary>
    /// Количество экземпляров компонента.
    /// </summary>
    public int Count
    {
        get; init;
    }
}
