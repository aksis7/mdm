using Avalonia;

namespace MiniPdm.Desktop;

internal static class Program
{
    /// <summary>
    /// Запускает приложение Avalonia с классическим временем жизни настольного приложения.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Создаёт конфигурацию приложения Avalonia и подключает загрузчик XAML.
    /// </summary>
    /// <returns>Настроенный построитель приложения.</returns>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
