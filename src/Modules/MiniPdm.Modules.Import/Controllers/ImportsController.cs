using MiniPdm.Common.Exceptions;
using Resources = MiniPdm.Common.Resources;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MiniPdm.Modules.Import.Abstractions;
using MiniPdm.Modules.Import.Features.Queries.GetImportReport;
using MiniPdm.Modules.Import.Features.Commands.ImportPackage;
using MiniPdm.Modules.Import.Infrastructure.SourceFiles;
using MiniPdm.Modules.Import.Services;
using MiniPdm.Contracts.Modules.Import.DtoModels;
using Microsoft.Extensions.Logging;

namespace MiniPdm.Modules.Import.Controllers;

/// <summary>
/// Принимает CAD-пакеты и предоставляет отчёты об импорте.
/// </summary>
/// <param name="sender">Посредник команд импорта и запросов отчёта.</param>
/// <param name="uploads">Хранилище временных попыток загрузки.</param>
/// <param name="logger">Журнал ошибок и отклонённых запросов.</param>
[ApiController]
[Route("api/imports")]
public sealed class ImportsController(ISender sender, IImportUploadStorage uploads, ILogger<ImportsController> logger) : ControllerBase
{
    /// <summary>
    /// Принимает multipart-загрузку и запускает импорт файлов.
    /// </summary>
    /// <param name="importId">Идентификатор импорта для безопасного повтора запроса.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>HTTP-ответ с отчётом импорта либо сообщением об ошибке обработки.</returns>
    [HttpPost("{importId:guid}")]
    [RequestSizeLimit(64L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 64L * 1024 * 1024, ValueLengthLimit = 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(Guid importId, CancellationToken cancellationToken)
    {
        if (importId == Guid.Empty)
            throw new InputLogicException(Resources.InputLogicException.ImportIdRequired);
        var existingReport = await sender.Send(new GetImportReportQuery(importId), cancellationToken);
        if (existingReport is not null)
            return Ok(existingReport);
        if (!Request.HasFormContentType)
            throw new InputLogicException(Resources.InputLogicException.MultipartRequired);
        try
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            var files = form.Files.Select(f => new ImportUploadFile(f.FileName, f.OpenReadStream())).ToArray();
            try
            {
                await using var attempt = await uploads.StageAsync(importId, files, cancellationToken);
                var report = await sender.Send(new ImportPackageCommand(importId, attempt.SourceDescriptor), cancellationToken);
                return Ok(report);
            }
            finally
            {
                foreach (var file in files)
                    await file.Content.DisposeAsync();
            }
        }
        catch (ImportUploadValidationException ex) { throw new InputLogicException(ex.Message); }
        catch (ImportSaveException ex) { logger.LogWarning(ex, "Import saga outcome could not be confirmed for {ImportId}", importId); return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Import outcome could not be confirmed. Retry with the same import ID." }); }
        catch (InputLogicException) { throw; }
        catch (BusinessLogicException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (InvalidDataException ex) { logger.LogInformation(ex, "Rejected malformed import upload {ImportId}", importId); return BadRequest(new { error = Resources.InputLogicException.MalformedMultipart }); }
        catch (BadHttpRequestException ex) { logger.LogInformation(ex, "Rejected malformed import request {ImportId}", importId); return BadRequest(new { error = Resources.InputLogicException.MalformedMultipart }); }
        catch (Exception ex) { logger.LogError(ex, "Import {ImportId} failed", importId); return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Import could not be completed." }); }
    }

    /// <summary>
    /// Возвращает отчёт ранее завершённого импорта.
    /// </summary>
    /// <param name="importId">Идентификатор импорта.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>HTTP-ответ с отчётом или статусом отсутствия результата.</returns>
    [HttpGet("{importId:guid}")]
    public async Task<IActionResult> GetReport(Guid importId, CancellationToken cancellationToken)
    {
        if (importId == Guid.Empty)
            throw new InputLogicException(Resources.InputLogicException.ImportIdRequired);
        var report = await sender.Send(new GetImportReportQuery(importId), cancellationToken);
        return report is null ? NotFound() : Ok(report);
    }
}
