using MiniPdm.Modules.Import.Abstractions.Cad;
using MiniPdm.Modules.Import.DtoModels.Cad;

namespace MiniPdm.Modules.Import.Infrastructure.Cad;

/// <summary>
/// Открывает CAD-пакет, расположенный в каталоге с JSON-файлами.
/// </summary>
public sealed class FileJsonCadSourceAdapter : ICadSourceAdapter
{
    /// <summary>
    /// Ключ файлового JSON-адаптера в описателях источника.
    /// </summary>
    public const string SourceKind = "file-json";
    /// <summary>
    /// Ключ вида CAD-источника, который поддерживает данный адаптер.
    /// </summary>
    public string Kind => SourceKind;

    /// <inheritdoc />
    public Task<ICadSession> OpenAsync(CadSourceDescriptorDto descriptor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(descriptor.Kind, Kind, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Source kind '{descriptor.Kind}' is not supported by this adapter.", nameof(descriptor));
        if (string.IsNullOrWhiteSpace(descriptor.Location))
            throw new ArgumentException("A source directory is required.", nameof(descriptor));

        var fullPath = Path.GetFullPath(descriptor.Location);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"CAD source directory was not found: {fullPath}");

        ICadSession session = new FileJsonCadSession(fullPath);
        return Task.FromResult(session);
    }
}

/// <summary>
/// Сессия чтения CAD-документов из каталога.
/// </summary>
/// <param name="directory">Каталог с файлами сессии.</param>
internal sealed class FileJsonCadSession(string directory) : ICadSession
{
    /// <summary>
    /// Перечислитель документов файлового каталога.
    /// </summary>
    public ICadDocumentSource Source { get; } = new FileJsonCadDocumentSource(directory);
    /// <summary>
    /// Читатель JSON-документов файлового каталога.
    /// </summary>
    public ICadDocumentReader Reader { get; } = new FileJsonCadDocumentReader(directory);
    /// <summary>
    /// Освобождает ресурсы сессии; файловая реализация не удерживает дополнительные ресурсы.
    /// </summary>
    /// <returns>Завершённая задача освобождения.</returns>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
