using MiniPdm.Modules.Import.Abstractions.Cad;
using MiniPdm.Modules.Import.DtoModels.Cad;

namespace MiniPdm.Modules.Import.Abstractions;

/// <summary>
/// Поток содержимого загружаемого файла и его исходное имя.
/// </summary>
public sealed record ImportUploadFile(string FileName, Stream Content)
{
    /// <summary>
    /// Имя файла, переданное клиентом.
    /// </summary>
    public string FileName { get; init; } = FileName;

    /// <summary>
    /// Поток байтов загружаемого файла.
    /// </summary>
    public Stream Content { get; init; } = Content;
}

/// <summary>
/// Временная попытка загрузки, удерживающая staged-файлы до завершения импорта.
/// </summary>
public interface IImportUploadAttempt : IAsyncDisposable
{
    /// <summary>
    /// Описатель источника, по которому сервис читает загруженные файлы.
    /// </summary>
    CadSourceDescriptorDto SourceDescriptor
    {
        get;
    }
}

/// <summary>
/// Проверяет и временно сохраняет файлы HTTP-загрузки до обработки импорта.
/// </summary>
public interface IImportUploadStorage
{
    /// <summary>
    /// Создаёт временную попытку загрузки и выдаёт описатель её CAD-источника.
    /// </summary>
    /// <param name="importId">Идентификатор импорта.</param>
    /// <param name="files">Файлы и потоки их содержимого.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Задача с попыткой, которую следует освободить после обработки.</returns>
    Task<IImportUploadAttempt> StageAsync(Guid importId, IReadOnlyList<ImportUploadFile> files, CancellationToken ct);
}
