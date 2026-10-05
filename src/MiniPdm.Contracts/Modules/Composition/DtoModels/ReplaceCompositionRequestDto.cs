namespace MiniPdm.Contracts.Modules.Composition.DtoModels;

/// <summary>
/// Задаёт дочерний объект и его количество в составе.
/// </summary>
public sealed record CompositionItemDto
{
    /// <summary>
    /// Идентификатор дочернего объекта.
    /// </summary>
    public Guid ChildObjectId
    {
        get; init;
    }

    /// <summary>
    /// Количество дочернего объекта.
    /// </summary>
    public int Quantity
    {
        get; init;
    }
}

/// <summary>
/// Запрос на полную замену состава объекта.
/// Для выполнения обновления используется токен конкурентного доступа.
/// </summary>
public sealed record ReplaceCompositionRequestDto
{
    /// <summary>
    /// Новый список компонентов либо <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<CompositionItemDto>? Components
    {
        get; init;
    }

    /// <summary>
    /// Токен конкурентного доступа, полученный при чтении состава.
    /// </summary>
    public Guid ExpectedConcurrencyToken
    {
        get; init;
    }
}
