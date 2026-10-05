using MiniPdm.Modules.Import.Abstractions.Cad;
using MiniPdm.Modules.Import.DtoModels.Cad;

namespace MiniPdm.Modules.Import.Abstractions;

/// <summary>
/// Управляет долговременным хранением файлов, принятых при импорте.
/// </summary>
public interface IImportSourceStorage
{
    /// <summary>
    /// Переносит принятые файлы во временное долговременное хранилище импорта.
    /// </summary>
    /// <param name="importId">Идентификатор операции импорта.</param>
    /// <param name="source">Исходный CAD-источник.</param>
    /// <param name="acceptedFiles">Имена файлов, принятых проверкой.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Задача завершения переноса.</returns>
    Task PromoteAsync(Guid importId, CadSourceDescriptorDto source, IReadOnlyCollection<string> acceptedFiles, CancellationToken ct);
    /// <summary>
    /// Возвращает ссылку хранения файла принятого импорта.
    /// </summary>
    /// <param name="importId">Идентификатор операции импорта.</param>
    /// <param name="fileName">Имя файла.</param>
    /// <returns>Ссылка, сохраняемая в данных версии.</returns>
    string GetSourceReference(Guid importId, string fileName);
    /// <summary>
    /// Удаляет файлы импорта при подтверждённом откате операции.
    /// </summary>
    /// <param name="importId">Идентификатор операции импорта.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Задача завершения компенсации.</returns>
    Task CompensateAsync(Guid importId, CancellationToken ct);
}
