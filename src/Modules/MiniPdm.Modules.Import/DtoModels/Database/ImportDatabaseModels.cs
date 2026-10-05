using MiniPdm.Domain.Composition;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;

namespace MiniPdm.Modules.Import.DtoModels.Database;

/// <summary>
/// Ключи, по которым хранилище выбирает существующие объекты для импорта.
/// </summary>
public sealed record ImportLookupDto
{
    /// <summary>
    /// Обозначения объектов из пакета, используемые для поиска совпадений.
    /// </summary>
    public IReadOnlyCollection<string> Designations { get; init; } = default!;

    /// <summary>
    /// Нормализованные наименования стандартных деталей для поиска совпадений.
    /// </summary>
    public IReadOnlyCollection<string> NormalizedStandardNames { get; init; } = default!;
}
/// <summary>
/// Направленное ребро активного графа сборки между объектами.
/// </summary>
public sealed record ActiveGraphEdgeDto
{
    /// <summary>
    /// Идентификатор родительского объекта в ребре графа.
    /// </summary>
    public Guid ParentId
    {
        get; init;
    }

    /// <summary>
    /// Идентификатор дочернего объекта в ребре графа.
    /// </summary>
    public Guid ChildId
    {
        get; init;
    }
}
/// <summary>
/// Снимок найденных объектов и активной структуры для подготовки импорта.
/// </summary>
public sealed record ImportSnapshotDto
{
    /// <summary>
    /// Существующие объекты, совпавшие с ключами поиска импорта.
    /// </summary>
    public IReadOnlyList<PdmObject> ExistingObjects { get; init; } = default!;

    /// <summary>
    /// Рёбра текущего активного графа состава.
    /// </summary>
    public IReadOnlyList<ActiveGraphEdgeDto> CurrentGraph { get; init; } = default!;
}
/// <summary>
/// Назначение версии объекта текущей при записи импорта.
/// </summary>
public sealed record CurrentVersionAssignmentDto
{
    /// <summary>
    /// Объект, чей указатель текущей версии назначается.
    /// </summary>
    public PdmObject Object { get; init; } = default!;

    /// <summary>
    /// Версия, назначаемая текущей для объекта.
    /// </summary>
    public ObjectVersion Version { get; init; } = default!;
}
/// <summary>
/// Подготовленный набор изменений базы данных и отчёт для фиксации импорта.
/// </summary>
public sealed record ImportWritePlanDto
{
    /// <summary>
    /// Новые PDM-объекты, создаваемые транзакцией импорта.
    /// </summary>
    public IReadOnlyList<PdmObject> NewObjects { get; init; } = default!;

    /// <summary>
    /// Новые версии, добавляемые транзакцией импорта.
    /// </summary>
    public IReadOnlyList<ObjectVersion> NewVersions { get; init; } = default!;

    /// <summary>
    /// Назначения новых текущих версий существующим объектам.
    /// </summary>
    public IReadOnlyList<CurrentVersionAssignmentDto> CurrentVersions { get; init; } = default!;

    /// <summary>
    /// Сериализованный отчёт, сохраняемый вместе с результатом импорта.
    /// </summary>
    public string ReportJson { get; init; } = default!;

    /// <summary>
    /// Связи состава, удаляемые транзакцией при замене содержимого.
    /// </summary>
    public IReadOnlyList<BomLink>? RemovedLinks
    {
        get; init;
    }
}
/// <summary>
/// Известный исход транзакции импорта.
/// </summary>
public enum ImportCommitState
{
    /// <summary>
    /// Транзакция завершена успешно.
    /// </summary>
    Completed,
    /// <summary>
    /// Откат транзакции подтверждён.
    /// </summary>
    ConfirmedRollback,
    /// <summary>
    /// Исход фиксации нельзя достоверно установить.
    /// </summary>
    Unknown
}
/// <summary>
/// Сохранённый или восстановленный результат обработки операции импорта.
/// </summary>
public sealed record ImportPersistenceResultDto
{
    /// <summary>
    /// Известное состояние фиксации операции импорта.
    /// </summary>
    public ImportCommitState State
    {
        get; init;
    }

    /// <summary>
    /// Указывает, что возвращён ранее сохранённый результат для того же идентификатора.
    /// </summary>
    public bool Replayed
    {
        get; init;
    }

    /// <summary>
    /// Сериализованный отчёт завершённого импорта, если он доступен.
    /// </summary>
    public string? ReportJson
    {
        get; init;
    }

    /// <summary>
    /// Описание ошибки или неопределённого исхода, если оно есть.
    /// </summary>
    public string? Error
    {
        get; init;
    }
}
