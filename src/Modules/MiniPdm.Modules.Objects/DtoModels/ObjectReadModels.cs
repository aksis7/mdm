using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;

namespace MiniPdm.Modules.Objects.DtoModels;

/// <summary>
/// Строка внутренней проекции поиска объектов.
/// Содержит поля карточки, необходимые для выдачи страницы результатов.
/// </summary>
public sealed record ObjectSearchRowDto
{
    /// <summary>
    /// Идентификатор объекта.
    /// </summary>
    public Guid Id
    {
        get; init;
    }

    /// <summary>
    /// Тип объекта PDM.
    /// </summary>
    public PdmObjectType Type
    {
        get; init;
    }

    /// <summary>
    /// Обозначение сборки или детали.
    /// </summary>
    public string? Designation
    {
        get; init;
    }

    /// <summary>
    /// Отображаемое наименование объекта.
    /// </summary>
    public string? Name
    {
        get; init;
    }

    /// <summary>
    /// Идентификатор текущей версии, если она есть.
    /// </summary>
    public Guid? CurrentVersionId
    {
        get; init;
    }

    /// <summary>
    /// Номер текущей версии, если она есть.
    /// </summary>
    public int? VersionNumber
    {
        get; init;
    }

    /// <summary>
    /// Состояние текущей версии, если она есть.
    /// </summary>
    public VersionState? State
    {
        get; init;
    }

    /// <summary>
    /// Масса единицы объекта в килограммах, если известна.
    /// </summary>
    public decimal? UnitMassKg
    {
        get; init;
    }

    /// <summary>
    /// Токен для обнаружения устаревших изменений.
    /// </summary>
    public Guid ConcurrencyToken
    {
        get; init;
    }

    /// <summary>
    /// Показывает, что у объекта нет текущей версии.
    /// </summary>
    public bool NoCurrentVersion
    {
        get; init;
    }
}

/// <summary>
/// Страница внутренней проекции поиска объектов.
/// Метаданные позволяют продолжить постраничную загрузку результатов.
/// </summary>
public sealed record ObjectSearchPageDto
{
    /// <summary>
    /// Строки объектов на текущей странице.
    /// </summary>
    public IReadOnlyList<ObjectSearchRowDto> Items { get; init; } = default!;

    /// <summary>
    /// Смещение первой строки в полном результате.
    /// </summary>
    public int Offset
    {
        get; init;
    }

    /// <summary>
    /// Максимальное число строк страницы.
    /// </summary>
    public int Limit
    {
        get; init;
    }

    /// <summary>
    /// Указывает, что после страницы есть дополнительные строки.
    /// </summary>
    public bool HasMore
    {
        get; init;
    }
}

/// <summary>
/// Внутренняя проекция одной версии для карточки объекта.
/// Включает атрибуты версии и ссылку на исходный файл.
/// </summary>
public sealed record ObjectVersionReadRowDto
{
    /// <summary>
    /// Идентификатор записи версии.
    /// </summary>
    public Guid Id
    {
        get; init;
    }

    /// <summary>
    /// Порядковый номер версии внутри объекта.
    /// </summary>
    public int Version
    {
        get; init;
    }

    /// <summary>
    /// Состояние версии.
    /// </summary>
    public VersionState State
    {
        get; init;
    }

    /// <summary>
    /// Наименование, сохранённое в версии.
    /// </summary>
    public string? Name
    {
        get; init;
    }

    /// <summary>
    /// Материал версии, если задан.
    /// </summary>
    public string? Material
    {
        get; init;
    }

    /// <summary>
    /// Масса версии, если задана.
    /// </summary>
    public decimal? Mass
    {
        get; init;
    }

    /// <summary>
    /// Ссылка на импортированный исходный файл.
    /// </summary>
    public string? SourceReference
    {
        get; init;
    }
}

/// <summary>
/// Внутренняя проекция карточки объекта и списка его версий.
/// Выбранная версия отсутствует, если её нет или она не была запрошена.
/// </summary>
public sealed record ObjectCardReadRowDto
{
    /// <summary>
    /// Идентификатор объекта.
    /// </summary>
    public Guid Id
    {
        get; init;
    }

    /// <summary>
    /// Тип объекта PDM.
    /// </summary>
    public PdmObjectType Type
    {
        get; init;
    }

    /// <summary>
    /// Обозначение сборки или детали.
    /// </summary>
    public string? Designation
    {
        get; init;
    }

    /// <summary>
    /// Исходное имя стандартного изделия.
    /// </summary>
    public string? StandardName
    {
        get; init;
    }

    /// <summary>
    /// Идентификатор текущей версии, если она есть.
    /// </summary>
    public Guid? CurrentVersionId
    {
        get; init;
    }

    /// <summary>
    /// Токен конкурентности объекта.
    /// </summary>
    public Guid ConcurrencyToken
    {
        get; init;
    }

    /// <summary>
    /// Краткие сведения обо всех версиях объекта.
    /// </summary>
    public IReadOnlyList<ObjectVersionSummaryReadRowDto> Versions { get; init; } = default!;

    /// <summary>
    /// Полные данные запрошенной или текущей версии.
    /// </summary>
    public ObjectVersionReadRowDto? SelectedVersion
    {
        get; init;
    }
}

/// <summary>
/// Краткая внутренняя проекция версии для списка в карточке объекта.
/// </summary>
public sealed record ObjectVersionSummaryReadRowDto
{
    /// <summary>
    /// Идентификатор записи версии.
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
    public VersionState State
    {
        get; init;
    }
}
