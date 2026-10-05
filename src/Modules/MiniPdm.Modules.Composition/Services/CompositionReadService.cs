using Resources = MiniPdm.Common.Resources;
using Microsoft.EntityFrameworkCore;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Modules.Composition.DtoModels;
using MiniPdm.Storage;
using MiniPdm.Contracts.Modules.Composition.DtoModels;
using Npgsql;

namespace MiniPdm.Modules.Composition.Services;

/// <summary>
/// Читает дерево состава одним рекурсивным запросом PostgreSQL.
/// Для каждого вхождения возвращает путь, количество и данные текущей версии.
/// </summary>
/// <param name="context">Контекст базы данных для выполнения запроса.</param>
public sealed class CompositionReadService(PdmDbContext context)
{
    private const string Sql = """
        WITH RECURSIVE composition AS (
            SELECT
                o."Id" AS "ObjectId",
                ARRAY[o."Id"]::uuid[] AS "ObjectPath",
                NULL::uuid[] AS "ParentPath",
                1::integer AS "LocalQuantity",
                o."Type" AS "TypeValue",
                o."Designation" AS "Designation",
                CASE WHEN o."Type" = 3 THEN o."StandardName" ELSE v."Name" END AS "Name",
                v."Material" AS "Material",
                v."Id" AS "VersionId",
                v."Version" AS "VersionNumber",
                v."State" AS "StateValue",
                CASE WHEN o."Type" = 1 THEN NULL::numeric ELSE v."Mass" END AS "UnitMassKg",
                false AS "IsCycle"
            FROM pdm_objects AS o
            LEFT JOIN object_versions AS v
                ON v."Id" = o."CurrentVersionId"
                AND v."ObjectId" = o."Id"
                AND v."State" <> 3
            WHERE o."Id" = @rootObjectId

            UNION ALL

            SELECT
                child."Id" AS "ObjectId",
                parent."ObjectPath" || child."Id" AS "ObjectPath",
                parent."ObjectPath" AS "ParentPath",
                link."Quantity" AS "LocalQuantity",
                child."Type" AS "TypeValue",
                child."Designation" AS "Designation",
                CASE WHEN child."Type" = 3 THEN child."StandardName" ELSE childVersion."Name" END AS "Name",
                childVersion."Material" AS "Material",
                childVersion."Id" AS "VersionId",
                childVersion."Version" AS "VersionNumber",
                childVersion."State" AS "StateValue",
                CASE WHEN child."Type" = 1 THEN NULL::numeric ELSE childVersion."Mass" END AS "UnitMassKg",
                child."Id" = ANY(parent."ObjectPath") AS "IsCycle"
            FROM composition AS parent
            JOIN bom_links AS link ON link."ParentVersionId" = parent."VersionId"
            JOIN pdm_objects AS child ON child."Id" = link."ChildObjectId"
            LEFT JOIN object_versions AS childVersion
                ON childVersion."Id" = child."CurrentVersionId"
                AND childVersion."ObjectId" = child."Id"
                AND childVersion."State" <> 3
            WHERE parent."TypeValue" = 1
                AND parent."VersionId" IS NOT NULL
                AND NOT parent."IsCycle"
        )
        SELECT * FROM composition
        ORDER BY cardinality("ObjectPath"), "ObjectPath"
        """;

    /// <summary>
    /// Получает дерево состава корневого объекта и преобразует его в API DTO.
    /// </summary>
    /// <param name="rootObjectId">Идентификатор корневого объекта.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Дерево состава либо <see langword="null"/>, если корневой объект отсутствует.</returns>
    public async Task<CompositionTreeDto?> GetCompositionAsync(Guid rootObjectId, CancellationToken cancellationToken)
    {
        var occurrences = await ReadAsync(rootObjectId, cancellationToken);
        return occurrences.Count == 0 ? null : MapTreeDto(rootObjectId, occurrences);
    }

    /// <summary>
    /// Преобразует строки вхождений в узлы публичного дерева состава.
    /// Для отсутствующих текущих версий и циклов добавляет диагностические сведения.
    /// </summary>
    /// <param name="rootObjectId">Идентификатор корневого объекта.</param>
    /// <param name="occurrences">Прочитанные вхождения в порядке обхода дерева.</param>
    /// <returns>Дерево состава с узлами и диагностиками.</returns>
    public static CompositionTreeDto MapTreeDto(Guid rootObjectId, IReadOnlyList<CompositionOccurrenceDto> occurrences)
    {
        var nodes = occurrences.Select(occurrence =>
        {
            var (errorCode, error) = GetDiagnostic(occurrence);
            return new CompositionNodeDto
            {
                ObjectId = occurrence.ObjectId,
                ObjectPath = occurrence.ObjectPath,
                ParentPath = occurrence.ParentPath,
                LocalQuantity = occurrence.LocalQuantity,
                Type = ToTypeName(occurrence.Type),
                Designation = occurrence.Designation,
                Name = occurrence.Name,
                Material = occurrence.Material,
                VersionId = occurrence.VersionId,
                VersionNumber = occurrence.VersionNumber,
                State = occurrence.State is VersionState state ? ToStateName(state) : null,
                UnitMassKg = occurrence.UnitMassKg,
                ErrorCode = errorCode,
                Error = error
            };
        }).ToArray();
        return new CompositionTreeDto
        {
            RootObjectId = rootObjectId,
            Nodes = nodes
        };
    }

    private static (string? ErrorCode, string? Error) GetDiagnostic(CompositionOccurrenceDto occurrence)
    {
        if (occurrence.IsCycle)
            return ("Cycle", $"Composition cycle detected along path {string.Join(" → ", occurrence.ObjectPath)}.");
        return occurrence.VersionId is null
            ? ("NoCurrentVersion", Resources.BusinessLogicException.NoCurrentVersion)
            : (null, null);
    }

    private static string ToTypeName(PdmObjectType type) => type switch
    {
        PdmObjectType.Assembly => "Assembly",
        PdmObjectType.Part => "Part",
        PdmObjectType.StandardPart => "StandardPart",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown PDM object type.")
    };

    private static string ToStateName(VersionState state) => state switch
    {
        VersionState.InWork => "InWork",
        VersionState.Approved => "Approved",
        VersionState.Cancelled => "Cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown version state.")
    };

    /// <summary>
    /// Читает строки дерева состава без преобразования в публичный DTO.
    /// Метод используется расчётами, которым нужны исходные пути и атрибуты объектов.
    /// </summary>
    /// <param name="rootObjectId">Идентификатор корневого объекта.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Упорядоченный список вхождений дерева, включая корневое.</returns>
    public async Task<IReadOnlyList<CompositionOccurrenceDto>> ReadAsync(Guid rootObjectId, CancellationToken cancellationToken)
    {
        var rows = await context.Database.SqlQueryRaw<CompositionRow>(Sql, new NpgsqlParameter("rootObjectId", rootObjectId))
            .ToListAsync(cancellationToken);

        return rows.Select(row => new CompositionOccurrenceDto
        {
            ObjectId = row.ObjectId,
            ObjectPath = row.ObjectPath,
            ParentPath = row.ParentPath,
            LocalQuantity = row.LocalQuantity,
            Type = (PdmObjectType)row.TypeValue,
            Designation = row.Designation,
            Name = row.Name,
            Material = row.Material,
            VersionId = row.VersionId,
            VersionNumber = row.VersionNumber,
            State = row.StateValue is int state ? (VersionState)state : null,
            UnitMassKg = row.UnitMassKg,
            IsCycle = row.IsCycle
        }).ToArray();
    }

    /// <summary>
    /// Вспомогательная проекция результата SQL для EF Core.
    /// Числовые значения перечислений преобразуются в CLR-типы после чтения.
    /// </summary>
    private sealed class CompositionRow
    {
        /// <summary>
        /// Идентификатор объекта текущего вхождения.
        /// </summary>
        public Guid ObjectId
        {
            get; set;
        }
        /// <summary>
        /// Идентификаторы объектов от корня до текущего вхождения.
        /// </summary>
        public Guid[] ObjectPath { get; set; } = [];
        /// <summary>
        /// Идентификаторы объектов от корня до родительского вхождения.
        /// </summary>
        public Guid[]? ParentPath
        {
            get; set;
        }
        /// <summary>
        /// Количество текущего объекта относительно родителя.
        /// </summary>
        public int LocalQuantity
        {
            get; set;
        }
        /// <summary>
        /// Числовое значение типа объекта из результата SQL.
        /// </summary>
        public int TypeValue
        {
            get; set;
        }
        /// <summary>
        /// Обозначение объекта, если оно предусмотрено его типом.
        /// </summary>
        public string? Designation
        {
            get; set;
        }
        /// <summary>
        /// Имя объекта либо имя его текущей версии.
        /// </summary>
        public string? Name
        {
            get; set;
        }
        /// <summary>
        /// Материал текущей версии, если он задан.
        /// </summary>
        public string? Material
        {
            get; set;
        }
        /// <summary>
        /// Идентификатор текущей версии, если она существует.
        /// </summary>
        public Guid? VersionId
        {
            get; set;
        }
        /// <summary>
        /// Номер текущей версии, если она существует.
        /// </summary>
        public int? VersionNumber
        {
            get; set;
        }
        /// <summary>
        /// Числовое значение состояния версии из результата SQL.
        /// </summary>
        public int? StateValue
        {
            get; set;
        }
        /// <summary>
        /// Масса одной единицы объекта в килограммах, если она известна.
        /// </summary>
        public decimal? UnitMassKg
        {
            get; set;
        }
        /// <summary>
        /// Показывает, что объект образует цикл в текущем пути.
        /// </summary>
        public bool IsCycle
        {
            get; set;
        }
    }
}
