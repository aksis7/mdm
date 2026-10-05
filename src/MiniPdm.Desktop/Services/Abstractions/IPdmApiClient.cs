using MiniPdm.Contracts.Modules.Calculations.DtoModels;
using MiniPdm.Contracts.Modules.BackgroundTasks.DtoModels;
using MiniPdm.Contracts.Modules.Composition.DtoModels;
using MiniPdm.Contracts.Modules.Import.DtoModels;
using MiniPdm.Contracts.Modules.Objects.DtoModels;
using MiniPdm.Contracts.Modules.Versions.DtoModels;

namespace MiniPdm.Desktop.Services.Abstractions;

/// <summary>
/// Определяет асинхронные операции настольного клиента для чтения и изменения данных PDM через API.
/// </summary>
public interface IPdmApiClient
{
    /// <summary>
    /// Ищет объекты и возвращает страницу результатов с учётом строки поиска и смещения.
    /// </summary>
    /// <param name="search">Строка поиска; <see langword="null"/> означает поиск без фильтра.</param>
    /// <param name="offset">Число записей, пропускаемых перед страницей.</param>
    /// <param name="limit">Максимальное число записей на странице.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Страница найденных объектов и сведения о продолжении выдачи.</returns>
    Task<ObjectSearchPageDto> SearchObjectsAsync(string? search = null, int offset = 0, int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Загружает карточку объекта, при необходимости выбрав заданную версию.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта.</param>
    /// <param name="version">Номер версии или <see langword="null"/> для версии по умолчанию.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Карточка объекта с версиями и выбранной версией.</returns>
    Task<ObjectCardDto> GetObjectAsync(Guid objectId, int? version = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Загружает развёрнутое дерево состава объекта.
    /// </summary>
    /// <param name="objectId">Идентификатор корневого объекта.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Дерево вхождений состава.</returns>
    Task<CompositionTreeDto> GetCompositionAsync(Guid objectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Загружает состав конкретной версии объекта.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта.</param>
    /// <param name="version">Номер версии.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Редактируемый состав и маркер конкурентного изменения.</returns>
    Task<VersionCompositionDto> GetVersionCompositionAsync(Guid objectId, int version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Вычисляет массу, спецификацию и диагностику состава объекта.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Результат расчёта состава.</returns>
    Task<CompositionCalculationDto> GetCalculationAsync(Guid objectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Передаёт файлы CAD-пакета серверу импорта с заданным идентификатором операции.
    /// </summary>
    /// <param name="importId">Идентификатор операции, используемый для безопасного повтора.</param>
    /// <param name="filePaths">Пути к передаваемым файлам.</param>
    /// <param name="cancellationToken">Токен отмены передачи и чтения ответа.</param>
    /// <returns>Отчёт о принятии и обработке файлов.</returns>
    Task<ImportReportDto> ImportFilesAsync(Guid importId, IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Получает ранее сохранённый отчёт операции импорта.
    /// </summary>
    /// <param name="importId">Идентификатор операции импорта.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Отчёт операции.</returns>
    Task<ImportReportDto> GetImportReportAsync(Guid importId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Создаёт новую версию объекта на основе переданной версии.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта.</param>
    /// <param name="request">Параметры клонирования, включая номер версии и маркер конкурентного изменения.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Результат изменения версии.</returns>
    Task<VersionMutationDto> CloneVersionAsync(Guid objectId, CloneVersionRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Изменяет состояние указанной версии объекта.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта.</param>
    /// <param name="version">Номер изменяемой версии.</param>
    /// <param name="request">Новое состояние и маркер конкурентного изменения.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Результат изменения версии.</returns>
    Task<VersionMutationDto> ChangeVersionStateAsync(Guid objectId, int version,
        ChangeVersionStateRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Обновляет атрибуты указанной версии объекта.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта.</param>
    /// <param name="version">Номер изменяемой версии.</param>
    /// <param name="request">Новые атрибуты и маркер конкурентного изменения.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Результат изменения версии.</returns>
    Task<VersionMutationDto> UpdateVersionAttributesAsync(Guid objectId, int version,
        UpdateVersionAttributesRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Полностью заменяет состав указанной версии объекта.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта.</param>
    /// <param name="version">Номер изменяемой версии.</param>
    /// <param name="request">Новый состав и маркер конкурентного изменения.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Результат изменения версии.</returns>
    Task<VersionMutationDto> ReplaceCompositionAsync(Guid objectId, int version,
        ReplaceCompositionRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получает зарегистрированные фоновые задачи сервера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Список задач и их текущих состояний.</returns>
    Task<IReadOnlyList<BackgroundTaskDto>> GetBackgroundTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Изменяет интервал запуска фоновой задачи.
    /// </summary>
    /// <param name="taskId">Идентификатор задачи.</param>
    /// <param name="request">Новый интервал расписания.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Обновлённое описание задачи.</returns>
    Task<BackgroundTaskDto> UpdateBackgroundTaskScheduleAsync(string taskId,
        UpdateBackgroundTaskScheduleRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Запрашивает немедленный запуск фоновой задачи.
    /// </summary>
    /// <param name="taskId">Идентификатор задачи.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Подтверждение принятия команды на запуск.</returns>
    Task<BackgroundTaskRunAcceptedDto> RunBackgroundTaskAsync(string taskId,
        CancellationToken cancellationToken = default);
}
