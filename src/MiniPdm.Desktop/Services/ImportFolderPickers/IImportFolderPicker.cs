using Avalonia.Controls;

namespace MiniPdm.Desktop.Services.ImportFolderPickers;

/// <summary>
/// Определяет способ выбора папки с файлами для импорта.
/// </summary>
public interface IImportFolderPicker
{
    /// <summary>
    /// Предлагает пользователю выбрать папку и собирает поддерживаемые файлы в пакет.
    /// </summary>
    /// <param name="owner">Окно, относительно которого открывается диалог выбора.</param>
    /// <param name="cancellationToken">Токен отмены выбора.</param>
    /// <returns>Пакет выбранных файлов либо <see langword="null"/>, если пользователь отменил выбор.</returns>
    Task<SelectedImportPackage?> PickAsync(Window owner, CancellationToken cancellationToken = default);
}
