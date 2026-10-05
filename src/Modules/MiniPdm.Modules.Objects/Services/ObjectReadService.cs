using Resources = MiniPdm.Common.Resources;
using Microsoft.EntityFrameworkCore;
using MiniPdm.Contracts.Modules.Objects.DtoModels;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Modules.Objects.DtoModels;
using MiniPdm.Storage;

namespace MiniPdm.Modules.Objects.Services;

/// <summary>
/// Читает страницы поиска и карточки объектов через проекции хранилища.
/// Сервис преобразует внутренние строки данных в публичные контракты API.
/// </summary>
/// <param name="context">Контекст базы данных объектов и версий.</param>
public sealed class ObjectReadService(PdmDbContext context)
{
    /// <summary>
    /// Выполняет поиск объектов и формирует страницу публичного ответа.
    /// </summary>
    /// <param name="search">Подстрока для поиска по обозначению и имени.</param>
    /// <param name="offset">Число строк, пропускаемых перед страницей.</param>
    /// <param name="limit">Максимальное число объектов в странице.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Страница результатов поиска с метаданными постраничной выдачи.</returns>
    public async Task<MiniPdm.Contracts.Modules.Objects.DtoModels.ObjectSearchPageDto> SearchObjectsAsync(string search, int offset, int limit, CancellationToken cancellationToken)
    {
        var page = await SearchAsync(search, offset, limit, cancellationToken);
        return new MiniPdm.Contracts.Modules.Objects.DtoModels.ObjectSearchPageDto
        {
            Items = page.Items.Select(row => new ObjectSearchItemDto
            {
                Id = row.Id,
                Type = row.Type.ToString(),
                Designation = row.Designation,
                Name = row.Name,
                CurrentVersionId = row.CurrentVersionId,
                VersionNumber = row.VersionNumber,
                State = row.State?.ToString(),
                UnitMassKg = row.UnitMassKg,
                ConcurrencyToken = row.ConcurrencyToken,
                NoCurrentVersion = row.NoCurrentVersion
            }).ToArray(),
            Offset = page.Offset,
            Limit = page.Limit,
            HasMore = page.HasMore
        };
    }

    /// <summary>
    /// Получает публичную карточку объекта и выбранной версии.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта.</param>
    /// <param name="versionNumber">Номер версии для просмотра или <see langword="null"/> для текущей версии.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Карточка объекта либо <see langword="null"/>, если объект или выбранная версия не найдены.</returns>
    public async Task<ObjectCardDto?> GetObjectAsync(Guid objectId, int? versionNumber, CancellationToken cancellationToken)
    {
        var row = await GetAsync(objectId, versionNumber, cancellationToken);
        if (row is null || (versionNumber.HasValue && row.SelectedVersion is null))
            return null;
        var selected = row.SelectedVersion;
        var name = row.Type == PdmObjectType.StandardPart ? row.StandardName : selected?.Name;
        var current = row.CurrentVersionId;
        var selectedDto = selected is null ? null : new ObjectVersionDto
        {
            Id = selected.Id,
            Version = selected.Version,
            State = selected.State.ToString(),
            Name = row.Type == PdmObjectType.StandardPart ? row.StandardName : selected.Name,
            Material = selected.Material,
            UnitMassKg = row.Type == PdmObjectType.Assembly ? null : selected.Mass,
            SourceReference = selected.SourceReference,
            IsCurrent = selected.Id == current
        };
        var versions = row.Versions.Select(v => new ObjectVersionSummaryDto
        {
            Id = v.Id,
            Version = v.Version,
            State = v.State.ToString(),
            IsCurrent = v.Id == current
        }).ToArray();
        var noCurrent = current is null;
        return new ObjectCardDto
        {
            Id = row.Id,
            Type = row.Type.ToString(),
            Designation = row.Designation,
            Name = name,
            CurrentVersionId = current,
            ConcurrencyToken = row.ConcurrencyToken,
            SelectedVersion = selectedDto,
            Versions = versions,
            ErrorCode = noCurrent && versionNumber is null ? "NoCurrentVersion" : null,
            Error = noCurrent && versionNumber is null ? Resources.BusinessLogicException.CurrentVersionMissing : null
        };
    }

    /// <summary>
    /// Ищет объекты и возвращает внутреннюю страницу проекций.
    /// Запрос выбирает не более <paramref name="limit"/> строк и определяет наличие следующей страницы.
    /// </summary>
    /// <param name="search">Подстрока для поиска по обозначению и имени.</param>
    /// <param name="offset">Число строк, пропускаемых перед страницей.</param>
    /// <param name="limit">Максимальное число строк страницы.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Внутренняя страница строк объектов.</returns>
    public async Task<MiniPdm.Modules.Objects.DtoModels.ObjectSearchPageDto> SearchAsync(string search, int offset, int limit, CancellationToken cancellationToken)
    {
        var query = context.Objects.AsNoTracking();
        if (search.Length > 0)
        {
            if (context.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                var folded = search.ToLowerInvariant();
                query = query.Where(o =>
                    (o.Designation != null && o.Designation.ToLower().Contains(folded)) ||
                    (o.StandardName != null && o.StandardName.ToLower().Contains(folded)) ||
                    o.Versions.Any(v => v.Id == o.CurrentVersionId && v.State != VersionState.Cancelled && v.Name != null && v.Name.ToLower().Contains(folded)));
            }
            else
            {
                var pattern = $"%{EscapeLike(search)}%";
                query = query.Where(o =>
                    (o.Designation != null && EF.Functions.ILike(o.Designation, pattern, "\\")) ||
                    (o.StandardName != null && EF.Functions.ILike(o.StandardName, pattern, "\\")) ||
                    o.Versions.Any(v => v.Id == o.CurrentVersionId && v.State != VersionState.Cancelled && v.Name != null && EF.Functions.ILike(v.Name, pattern, "\\")));
            }
        }

        var rows = await query
            .OrderBy(o => o.Designation ?? o.StandardName ?? string.Empty)
            .ThenBy(o => o.Id)
            .Skip(offset)
            .Take(limit + 1)
            .Select(o => new ObjectSearchProjection(
                o.Id,
                o.Type,
                o.Designation,
                o.StandardName,
                o.ConcurrencyToken,
                o.Versions
                    .Where(v => v.Id == o.CurrentVersionId && v.State != VersionState.Cancelled)
                    .Select(v => new CurrentVersionProjection(v.Id, v.Version, v.State, v.Name, v.Mass))
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > limit;
        var items = rows.Take(limit).Select(row => new ObjectSearchRowDto
        {
            Id = row.Id,
            Type = row.Type,
            Designation = row.Designation,
            Name = row.Type == PdmObjectType.StandardPart ? row.StandardName : row.CurrentVersion?.Name,
            CurrentVersionId = row.CurrentVersion?.Id,
            VersionNumber = row.CurrentVersion?.Version,
            State = row.CurrentVersion?.State,
            UnitMassKg = row.Type == PdmObjectType.Assembly ? null : row.CurrentVersion?.Mass,
            ConcurrencyToken = row.ConcurrencyToken,
            NoCurrentVersion = row.CurrentVersion is null
        }).ToArray();

        return new MiniPdm.Modules.Objects.DtoModels.ObjectSearchPageDto
        {
            Items = items,
            Offset = offset,
            Limit = limit,
            HasMore = hasMore
        };
    }

    /// <summary>
    /// Читает внутреннюю проекцию карточки объекта и его версий.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта.</param>
    /// <param name="versionNumber">Номер версии для чтения или <see langword="null"/> для текущей версии.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Проекция карточки либо <see langword="null"/>, если объект не найден.</returns>
    public async Task<ObjectCardReadRowDto?> GetAsync(Guid objectId, int? versionNumber, CancellationToken cancellationToken)
    {
        var row = await context.Objects
            .AsNoTracking()
            .AsSingleQuery()
            .Where(o => o.Id == objectId)
            .Select(o => new ObjectCardProjection(
                o.Id,
                o.Type,
                o.Designation,
                o.StandardName,
                o.Versions
                    .Where(v => v.Id == o.CurrentVersionId && v.State != VersionState.Cancelled)
                    .Select(v => (Guid?)v.Id)
                    .FirstOrDefault(),
                o.ConcurrencyToken,
                o.Versions.OrderByDescending(v => v.Version)
                    .Select(v => new VersionSummaryProjection(v.Id, v.Version, v.State))
                    .ToList(),
                o.Versions.Where(v => versionNumber.HasValue
                        ? v.Version == versionNumber.Value
                        : v.Id == o.CurrentVersionId && v.State != VersionState.Cancelled)
                    .Select(v => new VersionProjection(v.Id, v.Version, v.State, v.Name, v.Material, v.Mass, v.SourceReference))
                    .FirstOrDefault()))
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new ObjectCardReadRowDto
        {
            Id = row.Id,
            Type = row.Type,
            Designation = row.Designation,
            StandardName = row.StandardName,
            CurrentVersionId = row.CurrentVersionId,
            ConcurrencyToken = row.ConcurrencyToken,
            Versions = row.Versions.Select(v => new ObjectVersionSummaryReadRowDto
            {
                Id = v.Id,
                Version = v.Version,
                State = v.State
            }).ToArray(),
            SelectedVersion = row.SelectedVersion is null ? null : new ObjectVersionReadRowDto
            {
                Id = row.SelectedVersion.Id,
                Version = row.SelectedVersion.Version,
                State = row.SelectedVersion.State,
                Name = row.SelectedVersion.Name,
                Material = row.SelectedVersion.Material,
                Mass = row.SelectedVersion.Mass,
                SourceReference = row.SelectedVersion.SourceReference
            }
        };
    }

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>
    /// Проекция полей текущей версии, используемых в строке поиска.
    /// </summary>
    private sealed record CurrentVersionProjection(Guid Id, int Version, VersionState State, string? Name, decimal? Mass)
    {
        /// <summary>
        /// Идентификатор текущей версии.
        /// </summary>
        public Guid Id { get; init; } = Id;

        /// <summary>
        /// Номер текущей версии.
        /// </summary>
        public int Version { get; init; } = Version;

        /// <summary>
        /// Состояние текущей версии.
        /// </summary>
        public VersionState State { get; init; } = State;

        /// <summary>
        /// Имя текущей версии.
        /// </summary>
        public string? Name { get; init; } = Name;

        /// <summary>
        /// Масса текущей версии, если она известна.
        /// </summary>
        public decimal? Mass { get; init; } = Mass;
    }

    /// <summary>
    /// Проекция объекта и его текущей версии для одной строки поиска.
    /// </summary>
    private sealed record ObjectSearchProjection(Guid Id, PdmObjectType Type, string? Designation, string? StandardName, Guid ConcurrencyToken, CurrentVersionProjection? CurrentVersion)
    {
        /// <summary>
        /// Идентификатор объекта.
        /// </summary>
        public Guid Id { get; init; } = Id;

        /// <summary>
        /// Тип объекта PDM.
        /// </summary>
        public PdmObjectType Type { get; init; } = Type;

        /// <summary>
        /// Обозначение сборки или детали.
        /// </summary>
        public string? Designation { get; init; } = Designation;

        /// <summary>
        /// Исходное имя стандартного изделия.
        /// </summary>
        public string? StandardName { get; init; } = StandardName;

        /// <summary>
        /// Токен конкурентности объекта.
        /// </summary>
        public Guid ConcurrencyToken { get; init; } = ConcurrencyToken;

        /// <summary>
        /// Проекция текущей версии или null, если действующей версии нет.
        /// </summary>
        public CurrentVersionProjection? CurrentVersion { get; init; } = CurrentVersion;
    }

    /// <summary>
    /// Проекция выбранной версии объекта для карточки.
    /// </summary>
    private sealed record VersionProjection(Guid Id, int Version, VersionState State, string? Name, string? Material, decimal? Mass, string? SourceReference)
    {
        /// <summary>
        /// Идентификатор версии.
        /// </summary>
        public Guid Id { get; init; } = Id;

        /// <summary>
        /// Номер версии внутри объекта.
        /// </summary>
        public int Version { get; init; } = Version;

        /// <summary>
        /// Состояние версии.
        /// </summary>
        public VersionState State { get; init; } = State;

        /// <summary>
        /// Наименование, записанное в версии.
        /// </summary>
        public string? Name { get; init; } = Name;

        /// <summary>
        /// Материал версии, если задан.
        /// </summary>
        public string? Material { get; init; } = Material;

        /// <summary>
        /// Масса версии, если задана.
        /// </summary>
        public decimal? Mass { get; init; } = Mass;

        /// <summary>
        /// Ссылка на исходный файл версии.
        /// </summary>
        public string? SourceReference { get; init; } = SourceReference;
    }

    /// <summary>
    /// Краткая проекция версии для истории в карточке объекта.
    /// </summary>
    private sealed record VersionSummaryProjection(Guid Id, int Version, VersionState State)
    {
        /// <summary>
        /// Идентификатор версии.
        /// </summary>
        public Guid Id { get; init; } = Id;

        /// <summary>
        /// Номер версии.
        /// </summary>
        public int Version { get; init; } = Version;

        /// <summary>
        /// Состояние версии.
        /// </summary>
        public VersionState State { get; init; } = State;
    }

    /// <summary>
    /// Полная проекция карточки объекта с историей и выбранной версией.
    /// </summary>
    private sealed record ObjectCardProjection(Guid Id, PdmObjectType Type, string? Designation, string? StandardName, Guid? CurrentVersionId, Guid ConcurrencyToken,
        List<VersionSummaryProjection> Versions, VersionProjection? SelectedVersion)
    {
        /// <summary>
        /// Идентификатор объекта.
        /// </summary>
        public Guid Id { get; init; } = Id;

        /// <summary>
        /// Тип объекта PDM.
        /// </summary>
        public PdmObjectType Type { get; init; } = Type;

        /// <summary>
        /// Обозначение сборки или детали.
        /// </summary>
        public string? Designation { get; init; } = Designation;

        /// <summary>
        /// Исходное имя стандартного изделия.
        /// </summary>
        public string? StandardName { get; init; } = StandardName;

        /// <summary>
        /// Идентификатор текущей версии, если она есть.
        /// </summary>
        public Guid? CurrentVersionId { get; init; } = CurrentVersionId;

        /// <summary>
        /// Токен конкурентности объекта.
        /// </summary>
        public Guid ConcurrencyToken { get; init; } = ConcurrencyToken;

        /// <summary>
        /// Краткие сведения об истории версий.
        /// </summary>
        public List<VersionSummaryProjection> Versions { get; init; } = Versions;

        /// <summary>
        /// Проекция запрошенной или текущей версии.
        /// </summary>
        public VersionProjection? SelectedVersion { get; init; } = SelectedVersion;
    }
}
