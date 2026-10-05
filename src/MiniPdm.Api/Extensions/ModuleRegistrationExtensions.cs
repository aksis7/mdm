using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using MiniPdm.Modules.BackgroundTasks.Extensions;
using MiniPdm.Modules.Calculations.Extensions;
using MiniPdm.Modules.Composition.Extensions;
using MiniPdm.Modules.Import.Extensions;
using MiniPdm.Modules.Objects.Extensions;
using MiniPdm.Modules.Versions.Extensions;

namespace MiniPdm.Api.Extensions;

/// <summary>
/// Содержит методы подключения модулей Mini-PDM к веб-приложению.
/// </summary>
public static class ModuleRegistrationExtensions
{
    /// <summary>
    /// Регистрирует сервисы и контроллеры функциональных модулей Mini-PDM.
    /// </summary>
    /// <param name="mvc">Конфигурация MVC, к которой подключаются модули.</param>
    /// <returns>Та же конфигурация MVC с зарегистрированными сервисами и частями приложения.</returns>
    public static IMvcBuilder AddPdmModules(this IMvcBuilder mvc)
    {
        mvc.Services.AddImportModule();
        mvc.Services.AddObjectsModule();
        mvc.Services.AddVersionsModule();
        mvc.Services.AddCompositionModule();
        mvc.Services.AddCalculationsModule();
        mvc.Services.AddBackgroundTasksModule();
        mvc.AddApplicationPart(typeof(MiniPdm.Modules.Import.Extensions.ModuleRegistration).Assembly)
            .AddApplicationPart(typeof(MiniPdm.Modules.Objects.Extensions.ModuleRegistration).Assembly)
            .AddApplicationPart(typeof(MiniPdm.Modules.Versions.Extensions.ModuleRegistration).Assembly)
            .AddApplicationPart(typeof(MiniPdm.Modules.Composition.Extensions.ModuleRegistration).Assembly)
            .AddApplicationPart(typeof(MiniPdm.Modules.Calculations.Extensions.ModuleRegistration).Assembly)
            .AddApplicationPart(typeof(MiniPdm.Modules.BackgroundTasks.Extensions.ModuleRegistration).Assembly);
        return mvc;
    }
}
