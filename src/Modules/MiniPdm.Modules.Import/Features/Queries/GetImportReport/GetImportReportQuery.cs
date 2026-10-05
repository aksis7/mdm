using MediatR;
using MiniPdm.Contracts.Modules.Import.DtoModels;
using MiniPdm.Modules.Import.Abstractions.Database;
using MiniPdm.Modules.Import.DtoModels.Database;
using System.Text.Json;

namespace MiniPdm.Modules.Import.Features.Queries.GetImportReport;

/// <summary>
/// Запрашивает сохранённый отчёт импорта.
/// </summary>
public sealed record GetImportReportQuery(Guid ImportId) : IRequest<ImportReportDto?>
{
    /// <summary>
    /// Идентификатор операции импорта, чей отчёт запрашивается.
    /// </summary>
    public Guid ImportId { get; init; } = ImportId;
}

/// <summary>
/// Загружает отчёт завершённого импорта из журнала операций.
/// </summary>
/// <param name="persistence">Сервис чтения записей журнала импорта.</param>
public sealed class GetImportReportQueryHandler(IImportDatabaseService persistence) : IRequestHandler<GetImportReportQuery, ImportReportDto?>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Возвращает отчёт, если импорт завершён и результат сохранён.
    /// </summary>
    /// <param name="request">Запрос с идентификатором операции.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Отчёт завершённого импорта или <see langword="null"/>, если он недоступен.</returns>
    public async Task<ImportReportDto?> Handle(GetImportReportQuery request, CancellationToken cancellationToken)
    {
        var result = await persistence.FindAsync(request.ImportId, cancellationToken);
        if (result is null || result.State != ImportCommitState.Completed || result.ReportJson is null)
            return null;
        return JsonSerializer.Deserialize<ImportReportDto>(result.ReportJson, JsonOptions);
    }
}
