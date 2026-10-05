namespace MiniPdm.Modules.Import.DtoModels.Cad;

/// <summary>
/// Описывает способ доступа к CAD-источнику и его расположение.
/// </summary>
public sealed record CadSourceDescriptorDto
{
    /// <summary>
    /// Ключ адаптера, способного открыть этот источник.
    /// </summary>
    public string Kind { get; init; } = default!;

    /// <summary>
    /// Путь или иной адрес источника.
    /// </summary>
    public string Location { get; init; } = default!;
}
