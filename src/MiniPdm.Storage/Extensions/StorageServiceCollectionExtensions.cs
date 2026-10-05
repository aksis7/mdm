using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MiniPdm.Storage.Extensions;

/// <summary>
/// Регистрирует зависимости слоя хранения данных.
/// </summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    ///     Регистрирует контекст PDM и фабрику контекстов для PostgreSQL.
    /// </summary>
    /// <param name="services">
    ///     Коллекция служб для регистрации зависимостей хранения.
    /// </param>
    /// <param name="configuration">
    ///     Конфигурация приложения со строкой подключения к базе данных.
    /// </param>
    /// <returns>
    ///     Та же коллекция служб для последовательной регистрации.
    /// </returns>
    public static IServiceCollection AddPdmStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PdmDatabase") ?? Environment.GetEnvironmentVariable("PDM_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Configure ConnectionStrings:PdmDatabase or PDM_CONNECTION_STRING.");
        services.AddDbContextFactory<PdmDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<PdmDbContext>>().CreateDbContext());
        return services;
    }
}
