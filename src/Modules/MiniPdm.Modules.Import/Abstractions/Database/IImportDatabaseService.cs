using MiniPdm.Modules.Import.DtoModels.Database;

namespace MiniPdm.Modules.Import.Abstractions.Database;

/// <summary>
/// Import-specific transaction boundary and idempotency journal operations.
/// </summary>
public interface IImportDatabaseService
{
    /// <summary>
    /// Выполняет идемпотентное сохранение импорта и связывает его транзакцию с продвижением файлов.
    /// </summary>
    /// <param name="importId">Стабильный идентификатор операции для журнала идемпотентности.</param>
    /// <param name="lookup">Ключи объектов, необходимые для загрузки снимка.</param>
    /// <param name="prepare">Создаёт план записи по снимку; перед продвижением файлов обязан проверить отмену.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Состояние фиксации, результат воспроизведения или сведения об ошибке.</returns>
    Task<ImportPersistenceResultDto> ExecuteAsync(Guid importId, ImportLookupDto lookup, Func<ImportSnapshotDto, CancellationToken, Task<ImportWritePlanDto>> prepare, CancellationToken ct);
    /// <summary>
    /// Ищет сохранённый результат операции импорта.
    /// </summary>
    /// <param name="id">Идентификатор операции.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Сохранённый результат либо <see langword="null"/>, если запись отсутствует.</returns>
    Task<ImportPersistenceResultDto?> FindAsync(Guid id, CancellationToken ct);
    /// <summary>
    /// Уточняет исход фиксации операции по журналу импорта.
    /// </summary>
    /// <param name="id">Идентификатор операции.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Подтверждённое состояние фиксации или неопределённый исход.</returns>
    Task<ImportPersistenceResultDto> ResolveAsync(Guid id, CancellationToken ct);
    /// <summary>
    /// Выполняет компенсацию только после подтверждённого отката.
    /// </summary>
    /// <param name="id">Идентификатор операции.</param>
    /// <param name="compensate">Действие удаления внешних данных.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns><see langword="true"/>, если компенсация была допустима и выполнена.</returns>
    Task<bool> CompensateIfRolledBackAsync(Guid id, Func<CancellationToken, Task> compensate, CancellationToken ct);
}
