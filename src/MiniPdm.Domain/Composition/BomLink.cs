using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;

namespace MiniPdm.Domain.Composition;

/// <summary>
///     Связывает объект-компонент со спецификацией родительской версии.
/// </summary>
public sealed class BomLink
{
    /// <summary>
    ///     Возвращает или задаёт версию, которой принадлежит строка компонента.
    /// </summary>
    public Guid ParentVersionId
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт идентификатор объекта-компонента.
    /// </summary>
    public Guid ChildObjectId
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт положительное количество экземпляров компонента.
    /// </summary>
    public int Quantity
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт навигационную ссылку на родительскую версию.
    /// </summary>
    public ObjectVersion? ParentVersion
    {
        get; set;
    }

    /// <summary>
    ///     Возвращает или задаёт навигационную ссылку на объект-компонент.
    /// </summary>
    public PdmObject? ChildObject
    {
        get; set;
    }
}
