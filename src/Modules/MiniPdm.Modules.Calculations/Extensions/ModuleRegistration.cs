using Microsoft.Extensions.DependencyInjection;
using MiniPdm.Modules.Calculations.Services;

namespace MiniPdm.Modules.Calculations.Extensions;

/// <summary>
/// Регистрирует службу расчёта массы и спецификации состава.
/// </summary>
public static class ModuleRegistration
{
    /// <summary>
    /// Добавляет службу расчётов с временем жизни на один HTTP-запрос.
    /// </summary>
    /// <param name="services">Коллекция служб приложения.</param>
    /// <returns>Та же коллекция для продолжения настройки контейнера.</returns>
    public static IServiceCollection AddCalculationsModule(this IServiceCollection services)
    {
        services.AddScoped<CompositionCalculationService>();
        return services;
    }
}
