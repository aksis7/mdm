using Microsoft.Extensions.DependencyInjection;
using MiniPdm.Modules.Composition.Services;

namespace MiniPdm.Modules.Composition.Extensions;

/// <summary>
/// Регистрирует службы чтения дерева и версий состава.
/// </summary>
public static class ModuleRegistration
{
    /// <summary>
    /// Добавляет службы чтения составов с временем жизни на один HTTP-запрос.
    /// </summary>
    /// <param name="services">Коллекция служб приложения.</param>
    /// <returns>Та же коллекция для продолжения настройки контейнера.</returns>
    public static IServiceCollection AddCompositionModule(this IServiceCollection services)
    {
        services.AddScoped<CompositionReadService>();
        services.AddScoped<VersionCompositionReadService>();
        return services;
    }
}
