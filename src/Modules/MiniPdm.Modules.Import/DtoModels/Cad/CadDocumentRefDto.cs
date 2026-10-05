namespace MiniPdm.Modules.Import.DtoModels.Cad;

/// <summary>
/// Ссылка на CAD-документ внутри источника.
/// </summary>
public sealed record CadDocumentRefDto
{
    /// <summary>
    /// Имя файла документа в CAD-источнике.
    /// </summary>
    public string FileName { get; init; } = default!;
}
