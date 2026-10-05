using MiniPdm.Modules.Import.DtoModels.Cad;

namespace MiniPdm.Modules.Import.Abstractions.Cad;

/// <summary>
/// Преобразует ссылку на CAD-файл в результат чтения документа.
/// </summary>
public interface ICadDocumentReader
{
    /// <summary>
    /// Читает описание CAD-документа.
    /// </summary>
    /// <param name="document">Ссылка с именем файла для чтения.</param>
    /// <param name="cancellationToken">Токен отмены чтения.</param>
    /// <returns>Результат с документом либо сведениями об ошибке чтения.</returns>
    Task<CadReadResultDto> ReadAsync(CadDocumentRefDto document, CancellationToken cancellationToken);
}
