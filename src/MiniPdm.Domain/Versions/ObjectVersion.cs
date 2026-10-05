using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Composition;

namespace MiniPdm.Domain.Versions;

/// <summary>
///     Состояния жизненного цикла версии объекта.
/// </summary>
public enum VersionState
{
    /// <summary>
    ///     Версию можно редактировать.
    /// </summary>
    InWork = 1,

    /// <summary>
    ///     Версия утверждена и неизменяема.
    /// </summary>
    Approved = 2,

    /// <summary>
    ///     Версия аннулирована и исключена из текущих расчётов.
    /// </summary>
    Cancelled = 3
}

/// <summary>
///     Нумерованный снимок атрибутов объекта и состава его компонентов.
/// </summary>
public sealed class ObjectVersion
{
    /// <summary>
    ///     Возвращает или задаёт идентификатор версии.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    ///     Возвращает или задаёт идентификатор объекта, которому принадлежит версия.
    /// </summary>
    public Guid ObjectId
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт навигационную ссылку на объект-владелец.
    /// </summary>
    public PdmObject? Object
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт строки компонентов этой версии.
    /// </summary>
    public ICollection<BomLink> Components { get; set; } = new List<BomLink>();

    /// <summary>
    ///     Возвращает или задаёт положительный номер версии в истории объекта.
    /// </summary>
    public int Version
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт состояние жизненного цикла версии.
    /// </summary>
    public VersionState State { get; set; } = VersionState.InWork;

    /// <summary>
    ///     Возвращает или задаёт отображаемое наименование версии, если оно применимо.
    /// </summary>
    public string? Name
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт материал детали.
    /// </summary>
    public string? Material
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт массу одного изделия в килограммах, если она применима и известна.
    /// </summary>
    public decimal? Mass
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт ссылку на исходный документ версии.
    /// </summary>
    public string? SourceReference
    {
        get; set;
    }

    /// <summary>
    ///     Проверяет, разрешён ли правилами переход между состояниями версии.
    /// </summary>
    /// <param name="from">
    ///     Текущее состояние версии.
    /// </param>
    /// <param name="to">
    ///     Запрошенное следующее состояние.
    /// </param>
    /// <returns>
    ///     <see langword="true"/>, если переход вперёд разрешён.
    /// </returns>
    public static bool CanTransition(VersionState from, VersionState to) =>
        (from, to) is (VersionState.InWork, VersionState.Approved)
            or (VersionState.InWork, VersionState.Cancelled)
            or (VersionState.Approved, VersionState.Cancelled);
}
