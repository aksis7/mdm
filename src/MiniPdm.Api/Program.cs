using MediatR;
using Microsoft.AspNetCore.Mvc;
using MiniPdm.Api.Extensions;
using MiniPdm.Api.Errors;
using MiniPdm.Storage.Extensions;
using MiniPdm.Modules.BackgroundTasks.Abstractions;
using MiniPdm.Modules.Import.Infrastructure.SourceFiles;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<LogicExceptionHandler>();
var handlerAssemblies = new[]
{
    typeof(Program).Assembly,
    typeof(MiniPdm.Modules.Import.Extensions.ModuleRegistration).Assembly,
    typeof(MiniPdm.Modules.Objects.Extensions.ModuleRegistration).Assembly,
    typeof(MiniPdm.Modules.Versions.Extensions.ModuleRegistration).Assembly,
    typeof(MiniPdm.Modules.Composition.Extensions.ModuleRegistration).Assembly,
    typeof(MiniPdm.Modules.Calculations.Extensions.ModuleRegistration).Assembly,
    typeof(MiniPdm.Modules.BackgroundTasks.Extensions.ModuleRegistration).Assembly
};
builder.Services.AddMediatR(options => options.RegisterServicesFromAssemblies(handlerAssemblies));
builder.Services.AddPdmStorage(builder.Configuration);
builder.Services.AddSingleton(new BackgroundTaskDefinition(
    "import-source-recovery",
    "Восстановление исходных файлов",
    1440,
    async (serviceProvider, cancellationToken) =>
    {
        var result = await serviceProvider.GetRequiredService<ImportSourceRecovery>().RecoverDetailedAsync(cancellationToken);
        var summary = $"Удалено записей: {result.RemovedCount}.";
        return new BackgroundTaskExecutionResult(summary, result.Errors);
    }));
builder.Services.AddControllers(options =>
{
    options.Filters.Add(new ProducesDefaultResponseTypeAttribute(typeof(ProblemDetails)));
}).AddPdmModules();

var app = builder.Build();
app.UseExceptionHandler();
app.MapControllers();
app.Run();

/// <summary>
/// Предоставляет точку входа веб-приложения.
/// </summary>
public partial class Program
{
}
