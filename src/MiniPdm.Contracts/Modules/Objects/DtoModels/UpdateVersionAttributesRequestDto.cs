namespace MiniPdm.Contracts.Modules.Objects.DtoModels;

/// <summary>
/// Содержит значения атрибутов версии, которые нужно обновить.
/// Токен позволяет проверить, что версия не изменилась после её чтения.
/// </summary>
public sealed record UpdateVersionAttributesRequestDto
{
    /// <summary>
    /// Новое наименование версии или <see langword="null"/>, если наименование менять не нужно.
    /// </summary>
    public string? Name
    {
        get; init;
    }

    /// <summary>
    /// Новый материал версии или <see langword="null"/>, если материал менять не нужно.
    /// </summary>
    public string? Material
    {
        get; init;
    }

    /// <summary>
    /// Новая масса единицы в килограммах или <see langword="null"/>, если массу менять не нужно.
    /// </summary>
    public decimal? Mass
    {
        get; init;
    }

    /// <summary>
    /// Токен конкурентного доступа, полученный при чтении версии.
    /// </summary>
    public Guid ExpectedConcurrencyToken
    {
        get; init;
    }
}
