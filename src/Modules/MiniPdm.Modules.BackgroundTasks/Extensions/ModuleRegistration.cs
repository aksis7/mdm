using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MiniPdm.Modules.BackgroundTasks.Abstractions;
using MiniPdm.Modules.BackgroundTasks.Abstractions.Database;
using MiniPdm.Modules.BackgroundTasks.Services.Database;
using MiniPdm.Modules.BackgroundTasks.Services;

namespace MiniPdm.Modules.BackgroundTasks.Extensions;

/// <summary>
/// Содержит методы регистрации зависимостей модуля фоновых задач.
/// </summary>
public static class ModuleRegistration
{
    /// <summary>
    /// Регистрирует координатор фоновых задач, его хранилище и службу жизненного цикла.
    /// </summary>
    /// <param name="services">Коллекция служб приложения.</param>
    /// <returns>Та же коллекция служб для продолжения настройки.</returns>
    public static IServiceCollection AddBackgroundTasksModule(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(BackgroundTaskCoordinatorOptions.Default);
        services.AddScoped<IBackgroundTaskDatabaseService, BackgroundTaskDatabaseService>();
        services.AddSingleton<BackgroundTaskCoordinator>();
        services.AddSingleton<IBackgroundTaskCoordinator>(sp => sp.GetRequiredService<BackgroundTaskCoordinator>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<BackgroundTaskCoordinator>());
        return services;
    }
}
