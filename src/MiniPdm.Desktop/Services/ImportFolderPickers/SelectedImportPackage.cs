namespace MiniPdm.Desktop.Services.ImportFolderPickers;

/// <summary>
/// A selected CAD packet whose files remain available until import is confirmed or abandoned.
/// </summary>
public sealed class SelectedImportPackage : IDisposable
{
    private IDisposable? _lease;

    /// <summary>
    /// Создаёт пакет файлов и принимает владение необязательной блокировкой ресурсов.
    /// </summary>
    /// <param name="filePaths">Полные пути к файлам пакета.</param>
    /// <param name="lease">Ресурс, освобождаемый после завершения работы с пакетом.</param>
    public SelectedImportPackage(IReadOnlyList<string> filePaths, IDisposable? lease = null)
    {
        FilePaths = filePaths ?? throw new ArgumentNullException(nameof(filePaths));
        _lease = lease;
    }

    /// <summary>
    /// Возвращает пути к файлам, доступным для импорта.
    /// </summary>
    public IReadOnlyList<string> FilePaths
    {
        get;
    }

    /// <summary>
    /// Освобождает ресурс пакета; повторные вызовы безопасны.
    /// </summary>
    public void Dispose() => Interlocked.Exchange(ref _lease, null)?.Dispose();
}
