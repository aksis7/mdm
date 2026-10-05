using Microsoft.EntityFrameworkCore;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Modules.Composition.DtoModels;
using MiniPdm.Contracts.Modules.Composition.DtoModels;
using MiniPdm.Storage;

namespace MiniPdm.Modules.Composition.Services;

/// <summary>
/// Читает состав и токен конкурентности выбранной версии объекта.
/// Элементы состава описывают дочерние объекты с их текущими версиями.
/// </summary>
/// <param name="context">Контекст базы данных для проекции состава.</param>
public sealed class VersionCompositionReadService(PdmDbContext context)
{
    /// <summary>
    /// Получает публичный DTO состава указанной версии.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта.</param>
    /// <param name="version">Номер версии.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>DTO состава версии либо <see langword="null"/>, если версия не найдена.</returns>
    public async Task<VersionCompositionDto?> GetVersionCompositionAsync(Guid objectId, int version,
        CancellationToken cancellationToken)
    {
        var row = await ReadAsync(objectId, version, cancellationToken);
        return row is null ? null : new VersionCompositionDto
        {
            ObjectId = row.ObjectId,
            Version = row.Version,
            ConcurrencyToken = row.ConcurrencyToken,
            Items = row.Items.Select(item => new VersionCompositionItemDto
            {
                ChildObjectId = item.ChildObjectId,
                Quantity = item.Quantity,
                Type = TypeName(item.Type),
                Designation = item.Designation,
                Name = item.Name,
                NoCurrentVersion = item.NoCurrentVersion
            }).ToArray()
        };
    }

    /// <summary>
    /// Читает внутреннюю проекцию состава версии без создания API DTO.
    /// </summary>
    /// <param name="objectId">Идентификатор объекта.</param>
    /// <param name="version">Номер версии.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Строка состава версии либо <see langword="null"/>, если версия не найдена.</returns>
    public async Task<VersionCompositionReadRowDto?> ReadAsync(Guid objectId, int version,
        CancellationToken cancellationToken)
    {
        var row = await context.Versions
            .AsNoTracking()
            .AsSingleQuery()
            .Where(x => x.ObjectId == objectId && x.Version == version)
            .Select(x => new VersionCompositionProjection(
                x.ObjectId,
                x.Version,
                x.Object!.ConcurrencyToken,
                x.Components.OrderBy(link => link.ChildObjectId)
                    .Select(link => new VersionCompositionItemProjection(
                        link.ChildObjectId,
                        link.Quantity,
                        link.ChildObject!.Type,
                        link.ChildObject.Designation,
                        link.ChildObject.Type == PdmObjectType.StandardPart
                            ? link.ChildObject.StandardName
                            : link.ChildObject.Versions
                                .Where(childVersion => childVersion.Id == link.ChildObject.CurrentVersionId &&
                                    childVersion.State != VersionState.Cancelled)
                                .Select(childVersion => childVersion.Name)
                                .FirstOrDefault(),
                        !link.ChildObject.Versions.Any(childVersion =>
                            childVersion.Id == link.ChildObject.CurrentVersionId &&
                            childVersion.State != VersionState.Cancelled)))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new VersionCompositionReadRowDto
            {
                ObjectId = row.ObjectId,
                Version = row.Version,
                ConcurrencyToken = row.ConcurrencyToken,
                Items = row.Items.Select(item => new VersionCompositionItemReadRowDto
                {
                    ChildObjectId = item.ChildObjectId,
                    Quantity = item.Quantity,
                    Type = item.Type,
                    Designation = item.Designation,
                    Name = item.Name,
                    NoCurrentVersion = item.NoCurrentVersion
                }).ToArray()
            };
    }

    private static string TypeName(PdmObjectType type) => type switch
    {
        PdmObjectType.Assembly => "Assembly",
        PdmObjectType.Part => "Part",
        PdmObjectType.StandardPart => "StandardPart",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown PDM object type.")
    };

    /// <summary>
    /// Результат проекции состава выбранной версии.
    /// </summary>
    private sealed record VersionCompositionProjection(Guid ObjectId, int Version, Guid ConcurrencyToken,
        List<VersionCompositionItemProjection> Items)
    {
        /// <summary>
        /// Идентификатор родительского объекта.
        /// </summary>
        public Guid ObjectId { get; init; } = ObjectId;

        /// <summary>
        /// Номер выбранной версии.
        /// </summary>
        public int Version { get; init; } = Version;

        /// <summary>
        /// Токен конкурентности родительского объекта.
        /// </summary>
        public Guid ConcurrencyToken { get; init; } = ConcurrencyToken;

        /// <summary>
        /// Строки состава выбранной версии.
        /// </summary>
        public List<VersionCompositionItemProjection> Items { get; init; } = Items;
    }

    /// <summary>
    /// Результат проекции одной дочерней строки состава.
    /// </summary>
    private sealed record VersionCompositionItemProjection(Guid ChildObjectId, int Quantity, PdmObjectType Type,
        string? Designation, string? Name, bool NoCurrentVersion)
    {
        /// <summary>
        /// Идентификатор дочернего объекта.
        /// </summary>
        public Guid ChildObjectId { get; init; } = ChildObjectId;

        /// <summary>
        /// Количество дочернего объекта в строке.
        /// </summary>
        public int Quantity { get; init; } = Quantity;

        /// <summary>
        /// Тип дочернего объекта PDM.
        /// </summary>
        public PdmObjectType Type { get; init; } = Type;

        /// <summary>
        /// Обозначение дочерней сборки или детали.
        /// </summary>
        public string? Designation { get; init; } = Designation;

        /// <summary>
        /// Имя дочернего объекта или его текущей версии.
        /// </summary>
        public string? Name { get; init; } = Name;

        /// <summary>
        /// Показывает, что дочерний объект не имеет текущей версии.
        /// </summary>
        public bool NoCurrentVersion { get; init; } = NoCurrentVersion;
    }
}
