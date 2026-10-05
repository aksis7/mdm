using Microsoft.Extensions.DependencyInjection;
using MiniPdm.Modules.Versions.Abstractions;
using MiniPdm.Modules.Versions.Services;

namespace MiniPdm.Modules.Versions.Extensions;

/// <summary>
/// Методы регистрации сервисов модуля версий.
/// </summary>
public static class ModuleRegistration
{
    /// <summary>
    /// Добавляет в контейнер сервис изменения версий.
    /// </summary>
    /// <param name="services">Коллекция регистраций зависимостей приложения.</param>
    /// <returns>Та же коллекция для последовательной регистрации модулей.</returns>
    public static IServiceCollection AddVersionsModule(this IServiceCollection services)
    {
        services.AddScoped<IVersionMutationService, VersionMutationService>();
        return services;
    }
}
