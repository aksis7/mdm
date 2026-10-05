using MediatR;
using MiniPdm.Contracts.Modules.Import.DtoModels;
using MiniPdm.Modules.Import.Abstractions.Cad;
using MiniPdm.Modules.Import.DtoModels.Cad;
using MiniPdm.Modules.Import.Services;

namespace MiniPdm.Modules.Import.Features.Commands.ImportPackage;

/// <summary>
/// Запрашивает чтение, проверку и сохранение пакета CAD-документов.
/// </summary>
public sealed record ImportPackageCommand(Guid ImportId, CadSourceDescriptorDto Source) : IRequest<ImportReportDto>
{
    /// <summary>
    /// Идентификатор импорта для идемпотентной обработки.
    /// </summary>
    public Guid ImportId { get; init; } = ImportId;

    /// <summary>
    /// Описатель CAD-источника пакета.
    /// </summary>
    public CadSourceDescriptorDto Source { get; init; } = Source;
}

/// <summary>
/// Передаёт команду импорта прикладному сервису.
/// </summary>
/// <param name="service">Сервис чтения и сохранения импорта.</param>
public sealed class ImportPackageCommandHandler(ImportService service) : IRequestHandler<ImportPackageCommand, ImportReportDto>
{
    /// <summary>
    /// Выполняет импорт указанного источника.
    /// </summary>
    /// <param name="request">Команда с идентификатором импорта и источником.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача с отчётом о результатах обработки файлов.</returns>
    public Task<ImportReportDto> Handle(ImportPackageCommand request, CancellationToken cancellationToken) =>
        service.ExecuteAsync(request.ImportId, request.Source, cancellationToken);
}
