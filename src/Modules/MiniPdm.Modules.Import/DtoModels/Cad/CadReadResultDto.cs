namespace MiniPdm.Modules.Import.DtoModels.Cad;

/// <summary>
/// Результат чтения CAD-файла, содержащий документ или описание ошибки.
/// </summary>
public sealed record CadReadResultDto
{
    /// <summary>
    /// Прочитанный документ либо <see langword="null"/>, если чтение не удалось.
    /// </summary>
    public CadDocumentDto? Document
    {
        get; init;
    }

    /// <summary>
    /// Описание ошибки чтения либо <see langword="null"/> при успехе.
    /// </summary>
    public string? Error
    {
        get; init;
    }
}
