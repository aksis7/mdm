using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MiniPdm.Desktop.Services;
using MiniPdm.Desktop.Services.Abstractions;
using MiniPdm.Desktop.ViewModels;
using MiniPdm.Desktop.Services.ImportFolderPickers;

namespace MiniPdm.Desktop;

/// <summary>
/// Настраивает приложение и создаёт главное окно с необходимыми сервисами.
/// </summary>
public partial class App : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var options = PdmApiClientOptions.FromEnvironment();
            var httpClient = new HttpClient
            {
                BaseAddress = options.BaseAddress,
                Timeout = TimeSpan.FromMinutes(5)
            };
            IPdmApiClient client = new PdmApiClient(httpClient);
            BrowserImportFolderPicker? browserBridge = null;
            IImportFolderPicker folderPicker;
            if (string.Equals(Environment.GetEnvironmentVariable("PDM_BROWSER_PICKER"), "true", StringComparison.OrdinalIgnoreCase))
            {
                browserBridge = new BrowserImportFolderPicker();
                Task.Run(() => browserBridge.StartAsync()).GetAwaiter().GetResult();
                folderPicker = new ImportFolderPicker(browserBridge);
            }
            else
            {
                folderPicker = new ImportFolderPicker();
            }
            var mainWindow = new MainWindow { DataContext = new MainWindowViewModel(client), FolderPicker = folderPicker };
            mainWindow.Closed += (_, _) => (mainWindow.DataContext as IDisposable)?.Dispose();
            desktop.Exit += (_, _) =>
            {
                httpClient.Dispose();
                if (browserBridge is not null)
                    Task.Run(async () => await browserBridge.DisposeAsync()).GetAwaiter().GetResult();
            };
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
