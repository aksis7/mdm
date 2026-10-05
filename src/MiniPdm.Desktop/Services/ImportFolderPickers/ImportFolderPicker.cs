using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace MiniPdm.Desktop.Services.ImportFolderPickers;

/// <summary>
/// Выбирает CAD-файлы через системный диалог или браузерный мост.
/// </summary>
public sealed class ImportFolderPicker : IImportFolderPicker
{
    private readonly BrowserImportFolderPicker? _browser;

    /// <summary>
    /// Создаёт средство выбора папки с необязательным браузерным мостом.
    /// </summary>
    /// <param name="browser">Запущенный мост выбора папки из браузера; при отсутствии используется системный диалог.</param>
    public ImportFolderPicker(BrowserImportFolderPicker? browser = null) => _browser = browser;

    /// <inheritdoc />
    public async Task<SelectedImportPackage?> PickAsync(Window owner, CancellationToken cancellationToken = default)
    {
        if (_browser is null)
            return await PickNativeAsync(owner, cancellationToken);
        return await _browser.PickAsync(cancellationToken);
    }

    private static async Task<SelectedImportPackage?> PickNativeAsync(Window owner, CancellationToken cancellationToken)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите папку CAD-документов",
            AllowMultiple = false
        });
        cancellationToken.ThrowIfCancellationRequested();
        var folder = folders.FirstOrDefault();
        if (folder is null)
            return null;

        var paths = new List<string>();
        await foreach (var item in folder.GetItemsAsync().WithCancellation(cancellationToken))
        {
            if (item is not IStorageFile || item.TryGetLocalPath() is not { } path)
                continue;
            var extension = Path.GetExtension(path);
            if (extension.Equals(".a3d", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".m3d", StringComparison.OrdinalIgnoreCase))
                paths.Add(path);
        }

        return new SelectedImportPackage(paths
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }
}
