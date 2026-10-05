namespace MiniPdm.Contracts.Modules.Objects.DtoModels;

/// <summary>
/// Страница результатов поиска объектов.
/// Значения смещения и размера страницы позволяют продолжить чтение списка.
/// </summary>
public sealed record ObjectSearchPageDto
{
    /// <summary>
    /// Найденные объекты на текущей странице.
    /// </summary>
    public IReadOnlyList<ObjectSearchItemDto> Items { get; init; } = default!;

    /// <summary>
    /// Число записей, пропущенных перед этой страницей.
    /// </summary>
    public int Offset
    {
        get; init;
    }

    /// <summary>
    /// Максимальное число записей, запрошенное для страницы.
    /// </summary>
    public int Limit
    {
        get; init;
    }

    /// <summary>
    /// Указывает, есть ли записи после текущей страницы.
    /// </summary>
    public bool HasMore
    {
        get; init;
    }
}

/// <summary>
/// Краткие сведения об объекте, найденном при поиске.
/// </summary>
public sealed record ObjectSearchItemDto
{
    /// <summary>
    /// Идентификатор объекта.
    /// </summary>
    public Guid Id
    {
        get; init;
    }

    /// <summary>
    /// Тип объекта, например сборка, деталь или стандартное изделие.
    /// </summary>
    public string Type { get; init; } = default!;

    /// <summary>
    /// Обозначение объекта либо <see langword="null"/>.
    /// </summary>
    public string? Designation
    {
        get; init;
    }

    /// <summary>
    /// Наименование объекта либо <see langword="null"/>.
    /// </summary>
    public string? Name
    {
        get; init;
    }

    /// <summary>
    /// Идентификатор текущей версии либо <see langword="null"/>, если у объекта нет текущей версии.
    /// </summary>
    public Guid? CurrentVersionId
    {
        get; init;
    }

    /// <summary>
    /// Номер текущей версии либо <see langword="null"/>.
    /// </summary>
    public int? VersionNumber
    {
        get; init;
    }

    /// <summary>
    /// Состояние текущей версии либо <see langword="null"/>.
    /// </summary>
    public string? State
    {
        get; init;
    }

    /// <summary>
    /// Масса единицы текущей версии в килограммах либо <see langword="null"/>.
    /// </summary>
    public decimal? UnitMassKg
    {
        get; init;
    }

    /// <summary>
    /// Токен конкурентного доступа объекта.
    /// </summary>
    public Guid ConcurrencyToken
    {
        get; init;
    }

    /// <summary>
    /// Указывает, что у объекта отсутствует текущая версия.
    /// </summary>
    public bool NoCurrentVersion
    {
        get; init;
    }
}

/// <summary>
/// Карточка объекта с выбранной версией и перечнем его версий.
/// </summary>
public sealed record ObjectCardDto
{
    /// <summary>
    /// Идентификатор объекта.
    /// </summary>
    public Guid Id
    {
        get; init;
    }

    /// <summary>
    /// Тип объекта.
    /// </summary>
    public string Type { get; init; } = default!;

    /// <summary>
    /// Обозначение объекта либо <see langword="null"/>.
    /// </summary>
    public string? Designation
    {
        get; init;
    }

    /// <summary>
    /// Наименование объекта либо <see langword="null"/>.
    /// </summary>
    public string? Name
    {
        get; init;
    }

    /// <summary>
    /// Идентификатор текущей версии либо <see langword="null"/>.
    /// </summary>
    public Guid? CurrentVersionId
    {
        get; init;
    }

    /// <summary>
    /// Токен конкурентного доступа объекта.
    /// </summary>
    public Guid ConcurrencyToken
    {
        get; init;
    }

    /// <summary>
    /// Сведения о выбранной версии либо <see langword="null"/>.
    /// </summary>
    public ObjectVersionDto? SelectedVersion
    {
        get; init;
    }

    /// <summary>
    /// Список доступных версий объекта.
    /// </summary>
    public IReadOnlyList<ObjectVersionSummaryDto> Versions { get; init; } = default!;

    /// <summary>
    /// Код ошибки получения выбранной версии либо <see langword="null"/>.
    /// </summary>
    public string? ErrorCode
    {
        get; init;
    }

    /// <summary>
    /// Описание ошибки получения выбранной версии либо <see langword="null"/>.
    /// </summary>
    public string? Error
    {
        get; init;
    }
}

/// <summary>
/// Подробные сведения о версии объекта.
/// </summary>
public sealed record ObjectVersionDto
{
    /// <summary>
    /// Идентификатор версии.
    /// </summary>
    public Guid Id
    {
        get; init;
    }

    /// <summary>
    /// Порядковый номер версии.
    /// </summary>
    public int Version
    {
        get; init;
    }

    /// <summary>
    /// Состояние версии.
    /// </summary>
    public string State { get; init; } = default!;

    /// <summary>
    /// Наименование версии либо <see langword="null"/>.
    /// </summary>
    public string? Name
    {
        get; init;
    }

    /// <summary>
    /// Материал версии либо <see langword="null"/>.
    /// </summary>
    public string? Material
    {
        get; init;
    }

    /// <summary>
    /// Масса единицы в килограммах либо <see langword="null"/>.
    /// </summary>
    public decimal? UnitMassKg
    {
        get; init;
    }

    /// <summary>
    /// Ссылка на исходный файл либо <see langword="null"/>.
    /// </summary>
    public string? SourceReference
    {
        get; init;
    }

    /// <summary>
    /// Указывает, является ли версия текущей.
    /// </summary>
    public bool IsCurrent
    {
        get; init;
    }
}

/// <summary>
/// Краткие сведения о версии объекта для отображения в списке.
/// </summary>
public sealed record ObjectVersionSummaryDto
{
    /// <summary>
    /// Идентификатор версии.
    /// </summary>
    public Guid Id
    {
        get; init;
    }

    /// <summary>
    /// Порядковый номер версии.
    /// </summary>
    public int Version
    {
        get; init;
    }

    /// <summary>
    /// Состояние версии.
    /// </summary>
    public string State { get; init; } = default!;

    /// <summary>
    /// Указывает, является ли версия текущей.
    /// </summary>
    public bool IsCurrent
    {
        get; init;
    }
}
