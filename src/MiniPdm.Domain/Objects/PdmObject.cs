using MiniPdm.Domain.Versions;

namespace MiniPdm.Domain.Objects;

/// <summary>
///     Определяет типы объектов PDM, поддерживаемые приложением.
/// </summary>
public enum PdmObjectType
{
    /// <summary>
    ///     Сборка, масса которой рассчитывается по компонентам.
    /// </summary>
    Assembly = 1,

    /// <summary>
    ///     Деталь с обозначением, материалом и массой одного изделия.
    /// </summary>
    Part = 2,

    /// <summary>
    ///     Стандартное изделие, определяемое наименованием и массой одного изделия.
    /// </summary>
    StandardPart = 3
}

/// <summary>
///     Постоянная запись сборки, детали или стандартного изделия с историей версий.
/// </summary>
public sealed class PdmObject
{
    /// <summary>
    ///     Возвращает или задаёт постоянный идентификатор объекта.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    ///     Возвращает или задаёт тип объекта.
    /// </summary>
    public PdmObjectType Type
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт обозначение сборки или детали.
    /// </summary>
    public string? Designation
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт нормализованный ключ идентификации стандартного изделия по наименованию.
    /// </summary>
    public string? NormalizedName
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт отображаемое наименование стандартного изделия.
    /// </summary>
    public string? StandardName
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт идентификатор текущей версии объекта.
    /// </summary>
    public Guid? CurrentVersionId
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт навигационную ссылку на текущую версию.
    /// </summary>
    public ObjectVersion? CurrentVersion
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт версии, принадлежащие этому объекту.
    /// </summary>
    public ICollection<ObjectVersion> Versions { get; set; } = new List<ObjectVersion>();

    /// <summary>
    ///     Возвращает или задаёт токен, изменяемый при каждом сохранённом изменении объекта или версии.
    /// </summary>
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
