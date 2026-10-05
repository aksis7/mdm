using Microsoft.Extensions.DependencyInjection;
using MiniPdm.Modules.Import.Abstractions.Database;
using MiniPdm.Modules.Import.Abstractions;
using MiniPdm.Modules.Import.Abstractions.Cad;
using MiniPdm.Modules.Import.Infrastructure.Cad;
using MiniPdm.Modules.Import.Infrastructure.SourceFiles;
using MiniPdm.Modules.Import.Services;
using MiniPdm.Modules.Import.Services.Database;

namespace MiniPdm.Modules.Import.Extensions;

/// <summary>
/// Методы регистрации зависимостей модуля импорта.
/// </summary>
public static class ModuleRegistration
{
    /// <summary>
    /// Добавляет хранилища, адаптеры CAD-источников и сервисы импорта.
    /// </summary>
    /// <param name="services">Коллекция регистраций зависимостей приложения.</param>
    /// <returns>Та же коллекция для последовательной регистрации модулей.</returns>
    public static IServiceCollection AddImportModule(this IServiceCollection services)
    {
        services.AddOptions<ImportStorageOptions>().BindConfiguration("ImportStorage")
            .Validate(FileImportStorage.HasValidLimits, "Import upload limits are outside the allowed range.")
            .ValidateOnStart();
        services.AddSingleton<ICadSourceAdapter, FileJsonCadSourceAdapter>();
        services.AddSingleton<ICadSourceFactory, CadSourceFactory>();
        services.AddSingleton<FileImportStorage>();
        services.AddSingleton<IImportSourceStorage>(sp => sp.GetRequiredService<FileImportStorage>());
        services.AddSingleton<IImportUploadStorage>(sp => sp.GetRequiredService<FileImportStorage>());
        services.AddScoped<ImportSourceRecovery>();
        services.AddScoped<ImportService>();
        services.AddScoped<IImportDatabaseService, ImportDatabaseService>();
        return services;
    }
}
