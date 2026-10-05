namespace MiniPdm.Contracts.Modules.Versions.DtoModels;

/// <summary>
/// Результат изменения версии объекта.
/// История существующих версий сохраняется, а новая версия может стать текущей.
/// </summary>
public sealed record VersionMutationDto
{
    /// <summary>
    /// Идентификатор изменённого объекта.
    /// </summary>
    public Guid ObjectId
    {
        get; init;
    }

    /// <summary>
    /// Идентификатор созданной или изменённой версии.
    /// </summary>
    public Guid VersionId
    {
        get; init;
    }

    /// <summary>
    /// Номер версии.
    /// </summary>
    public int VersionNumber
    {
        get; init;
    }

    /// <summary>
    /// Состояние версии после изменения.
    /// </summary>
    public string State { get; init; } = default!;

    /// <summary>
    /// Идентификатор текущей версии после изменения либо <see langword="null"/>.
    /// </summary>
    public Guid? CurrentVersionId
    {
        get; init;
    }

    /// <summary>
    /// Новый токен конкурентного доступа объекта.
    /// </summary>
    public Guid ConcurrencyToken
    {
        get; init;
    }

    /// <summary>
    /// Предупреждения, выявленные при выполнении операции.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = default!;

}

/// <summary>
/// Описывает ошибку изменения версии, включая путь цикла, если он обнаружен.
/// </summary>
public sealed record VersionMutationErrorDto
{
    /// <summary>
    /// Машиночитаемый код ошибки.
    /// </summary>
    public string Code { get; init; } = default!;

    /// <summary>
    /// Понятное пользователю описание ошибки.
    /// </summary>
    public string Message { get; init; } = default!;

    /// <summary>
    /// Идентификаторы объектов, образующие цикл, либо <see langword="null"/>.
    /// </summary>
    public Guid[]? CyclePath
    {
        get; init;
    }
}
