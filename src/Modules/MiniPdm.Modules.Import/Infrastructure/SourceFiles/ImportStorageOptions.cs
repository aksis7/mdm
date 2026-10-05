namespace MiniPdm.Modules.Import.Infrastructure.SourceFiles;

/// <summary>
/// Ограничения и каталог долговременного хранения загружаемых CAD-пакетов.
/// </summary>
public sealed class ImportStorageOptions
{
    /// <summary>
    /// Жёсткий верхний предел количества файлов в одной загрузке.
    /// </summary>
    public const int HardMaxFiles = 1000;
    /// <summary>
    /// Жёсткий верхний предел размера одного файла в байтах.
    /// </summary>
    public const long HardMaxFileBytes = 8 * 1024 * 1024;
    /// <summary>
    /// Жёсткий верхний предел суммарного размера загрузки в байтах.
    /// </summary>
    public const long HardMaxTotalBytes = 64 * 1024 * 1024;
    /// <summary>
    /// Корневой каталог данных; при отсутствии используется каталог data рядом с приложением.
    /// </summary>
    public string? DataRoot
    {
        get; set;
    }
    /// <summary>
    /// Максимальное число файлов, принимаемых за одну загрузку.
    /// </summary>
    public int MaxFiles { get; set; } = HardMaxFiles;
    /// <summary>
    /// Максимальный размер одного загружаемого файла в байтах.
    /// </summary>
    public long MaxFileBytes { get; set; } = HardMaxFileBytes;
    /// <summary>
    /// Максимальный суммарный размер файлов одной загрузки в байтах.
    /// </summary>
    public long MaxTotalBytes { get; set; } = HardMaxTotalBytes;
}
