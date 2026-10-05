using MiniPdm.Domain.Composition;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;

namespace MiniPdm.Domain.Versions.Mutations;

/// <summary>
///     Результат планирования изменения версии.
/// </summary>
public enum VersionMutationStatus
{
    /// <summary>
    ///     Изменение прошло проверку и может быть сохранено.
    /// </summary>
    Succeeded,

    /// <summary>
    ///     Выбранный объект или версия не существует.
    /// </summary>
    NotFound,

    /// <summary>
    ///     Изменение конфликтует с текущим состоянием объекта.
    /// </summary>
    Conflict,

    /// <summary>
    ///     Запрос содержит недопустимые значения.
    /// </summary>
    Invalid
}

/// <summary>
///     Машиночитаемое и понятное пользователю описание отклонённого изменения.
/// </summary>
public sealed record VersionMutationError(string Code, string Message, Guid[]? CyclePath = null)
{
    /// <summary>
    ///     Стабильный код ошибки для вызывающей стороны.
    /// </summary>
    public string Code { get; init; } = Code;

    /// <summary>
    ///     Описание для показа пользователю.
    /// </summary>
    public string Message { get; init; } = Message;

    /// <summary>
    ///     Замкнутый путь объектов, если причиной ошибки стал цикл.
    /// </summary>
    public Guid[]? CyclePath { get; init; } = CyclePath;
}

/// <summary>
///     Загруженное состояние для планирования изменения версии без обращения к базе данных.
/// </summary>
public sealed record VersionMutationSnapshot(PdmObject Object, ObjectVersion SelectedVersion,
    IReadOnlyList<CompositionGraphEdge> CurrentGraph, IReadOnlySet<Guid> ExistingChildIds)
{
    /// <summary>
    ///     Объект, версия которого изменяется.
    /// </summary>
    public PdmObject Object { get; init; } = Object;

    /// <summary>
    ///     Версия, выбранная вызывающей стороной.
    /// </summary>
    public ObjectVersion SelectedVersion { get; init; } = SelectedVersion;

    /// <summary>
    ///     Связи действующего состава для проверки циклов.
    /// </summary>
    public IReadOnlyList<CompositionGraphEdge> CurrentGraph { get; init; } = CurrentGraph;

    /// <summary>
    ///     Идентификаторы объектов, которые можно добавить как компоненты.
    /// </summary>
    public IReadOnlySet<Guid> ExistingChildIds { get; init; } = ExistingChildIds;
}

/// <summary>
///     Предлагаемые изменения базы данных и диагностики изменения версии.
/// </summary>
public sealed record VersionMutationPlan(VersionMutationStatus Status, ObjectVersion? Version,
    Guid? DesiredCurrentVersionId, ObjectVersion? NewVersion, IReadOnlyList<BomLink> RemovedLinks,
    VersionMutationError? Error, IReadOnlyList<string> Warnings)
{
    /// <summary>
    ///     Результат операции.
    /// </summary>
    public VersionMutationStatus Status { get; init; } = Status;

    /// <summary>
    ///     Версия, возвращаемая операцией.
    /// </summary>
    public ObjectVersion? Version { get; init; } = Version;

    /// <summary>
    ///     Идентификатор текущей версии после применения плана.
    /// </summary>
    public Guid? DesiredCurrentVersionId { get; init; } = DesiredCurrentVersionId;

    /// <summary>
    ///     Новая версия для добавления при клонировании.
    /// </summary>
    public ObjectVersion? NewVersion { get; init; } = NewVersion;

    /// <summary>
    ///     Существующие строки компонентов для удаления.
    /// </summary>
    public IReadOnlyList<BomLink> RemovedLinks { get; init; } = RemovedLinks;

    /// <summary>
    ///     Описание причины отклонения плана.
    /// </summary>
    public VersionMutationError? Error { get; init; } = Error;

    /// <summary>
    ///     Предупреждения проверки, не препятствующие операции.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Warnings;
}

/// <summary>
///     Результат применения плана изменения версии.
/// </summary>
public sealed record VersionMutationResult(VersionMutationStatus Status, Guid ObjectId, Guid? VersionId,
    int? VersionNumber, VersionState? State, Guid? CurrentVersionId, Guid? ConcurrencyToken,
    VersionMutationError? Error, IReadOnlyList<string> Warnings)
{
    /// <summary>
    ///     Результат операции.
    /// </summary>
    public VersionMutationStatus Status { get; init; } = Status;

    /// <summary>
    ///     Идентификатор изменённого объекта.
    /// </summary>
    public Guid ObjectId { get; init; } = ObjectId;

    /// <summary>
    ///     Идентификатор изменённой версии, если он доступен.
    /// </summary>
    public Guid? VersionId { get; init; } = VersionId;

    /// <summary>
    ///     Номер изменённой версии, если он доступен.
    /// </summary>
    public int? VersionNumber { get; init; } = VersionNumber;

    /// <summary>
    ///     Состояние изменённой версии, если оно доступно.
    /// </summary>
    public VersionState? State { get; init; } = State;

    /// <summary>
    ///     Идентификатор текущей версии после изменения.
    /// </summary>
    public Guid? CurrentVersionId { get; init; } = CurrentVersionId;

    /// <summary>
    ///     Новый токен объекта после успешной записи.
    /// </summary>
    public Guid? ConcurrencyToken { get; init; } = ConcurrencyToken;

    /// <summary>
    ///     Описание причины отклонения изменения.
    /// </summary>
    public VersionMutationError? Error { get; init; } = Error;

    /// <summary>
    ///     Предупреждения проверки, не препятствующие операции.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Warnings;
}

/// <summary>
///     Компонент и его количество в новом составе сборки.
/// </summary>
public sealed record CompositionItem(Guid ChildObjectId, int Quantity)
{
    /// <summary>
    ///     Идентификатор объекта-компонента.
    /// </summary>
    public Guid ChildObjectId { get; init; } = ChildObjectId;

    /// <summary>
    ///     Положительное количество экземпляров компонента.
    /// </summary>
    public int Quantity { get; init; } = Quantity;
}
