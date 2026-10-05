using MiniPdm.Modules.Import.DtoModels.Cad;

namespace MiniPdm.Modules.Import.Abstractions.Cad;

/// <summary>
/// Предоставляет последовательность документов, входящих в CAD-источник.
/// </summary>
public interface ICadDocumentSource
{
    /// <summary>
    /// Перечисляет ссылки на документы источника.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены перечисления.</param>
    /// <returns>Асинхронная последовательность ссылок на CAD-документы.</returns>
    IAsyncEnumerable<CadDocumentRefDto> GetDocumentsAsync(CancellationToken cancellationToken);
}
