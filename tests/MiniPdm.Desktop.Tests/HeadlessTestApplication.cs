using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(MiniPdm.Desktop.Tests.HeadlessTestApplicationBuilder))]

namespace MiniPdm.Desktop.Tests;

/// <summary>
/// Создаёт конфигурацию Avalonia для запуска интерфейсных тестов без графической среды.
/// </summary>
public static class HeadlessTestApplicationBuilder
{
    /// <summary>
    /// Создаёт конфигурацию Avalonia с headless-платформой для запуска интерфейсных тестов без графической среды.
    /// </summary>
    /// <returns>Конфигурацию Avalonia, настроенную для запуска тестов без графической среды.</returns>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<HeadlessTestApplication>()
        .WithInterFont()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Предоставляет минимальное приложение Avalonia для интерфейсных тестов без графической среды.
/// </summary>
public sealed class HeadlessTestApplication : Application
{
    /// <inheritdoc/>
    public override void Initialize() => Styles.Add(new FluentTheme());
}
