namespace MiniPdm.Modules.Import.Abstractions.Cad;

/// <summary>
/// Объединяет перечислитель документов и средство их чтения на время работы с источником.
/// </summary>
public interface ICadSession : IAsyncDisposable
{
    /// <summary>
    /// Источник ссылок на документы текущей сессии.
    /// </summary>
    ICadDocumentSource Source
    {
        get;
    }
    /// <summary>
    /// Средство чтения документов текущей сессии.
    /// </summary>
    ICadDocumentReader Reader
    {
        get;
    }
}
