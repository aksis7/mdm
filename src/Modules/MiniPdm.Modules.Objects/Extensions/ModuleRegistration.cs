using Microsoft.Extensions.DependencyInjection;
using MiniPdm.Modules.Objects.Services;

namespace MiniPdm.Modules.Objects.Extensions;

/// <summary>
/// Регистрирует службы модуля чтения объектов в контейнере зависимостей.
/// </summary>
public static class ModuleRegistration
{
    /// <summary>
    /// Добавляет службы чтения объектов с временем жизни на один HTTP-запрос.
    /// </summary>
    /// <param name="services">Коллекция служб приложения.</param>
    /// <returns>Та же коллекция для продолжения настройки контейнера.</returns>
    public static IServiceCollection AddObjectsModule(this IServiceCollection services)
    {
        services.AddScoped<ObjectReadService>();
        return services;
    }
}
