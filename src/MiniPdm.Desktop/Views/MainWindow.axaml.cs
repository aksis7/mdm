using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MiniPdm.Desktop.Services.ImportFolderPickers;
using MiniPdm.Desktop.ViewModels;

namespace MiniPdm.Desktop;

/// <summary>
/// Главное окно приложения для просмотра объектов и запуска рабочих операций.
/// </summary>
public partial class MainWindow : Window
{
    private CancellationTokenSource? _pickerCancellation;
    private bool _isPicking;
    private bool _windowClosed;

    /// <summary>
    /// Получает или задаёт средство выбора папки для импорта файлов.
    /// </summary>
    public IImportFolderPicker FolderPicker { get; set; } = new ImportFolderPicker();

    /// <summary>
    /// Создаёт окно и связывает закрытие окна с отменой текущего выбора папки.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            _windowClosed = true;
            _pickerCancellation?.Cancel();
        };
    }

    private async void PickImportFolder(object? sender, RoutedEventArgs e)
    {
        if (_isPicking || DataContext is not MainWindowViewModel viewModel)
            return;
        _isPicking = true;
        var cancellation = new CancellationTokenSource();
        _pickerCancellation = cancellation;
        ImportFolderButton.SetCurrentValue(IsEnabledProperty, false);
        try
        {
            var package = await FolderPicker.PickAsync(this, cancellation.Token);
            if (package is null)
                return;
            MainTabs.SelectedItem = ImportReportTab;
            await viewModel.Import.ImportPackageAsync(package);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { viewModel.ReportError($"Не удалось прочитать выбранную папку: {ex.Message}"); }
        finally
        {
            _pickerCancellation = null;
            _isPicking = false;
            cancellation.Dispose();
            if (!_windowClosed)
                ImportFolderButton.SetCurrentValue(IsEnabledProperty, viewModel.Import.CanStartNewImport);
        }
    }

    private void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.RefreshCommand.Execute(null);
            e.Handled = true;
        }
    }
}
