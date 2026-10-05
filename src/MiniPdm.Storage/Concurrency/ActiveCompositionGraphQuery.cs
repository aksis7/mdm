using Microsoft.EntityFrameworkCore;
using MiniPdm.Domain.Composition;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;

namespace MiniPdm.Storage.Concurrency;

/// <summary>
/// Читает граф состава по текущим версиям сборок, которые не аннулированы.
/// </summary>
public static class ActiveCompositionGraphQuery
{
    /// <summary>
    ///     Загружает связи из текущих версий сборок, исключая аннулированные версии.
    /// </summary>
    /// <param name="context">
    ///     Контекст базы данных для выполнения запроса.
    /// </param>
    /// <param name="ct">
    ///     Токен отмены операции с базой данных.
    /// </param>
    /// <returns>
    ///     Связи действующего состава от родительских объектов к дочерним.
    /// </returns>
    public static Task<List<CompositionGraphEdge>> LoadAsync(PdmDbContext context, CancellationToken ct) =>
        (from parent in context.Objects.AsNoTracking()
         where parent.Type == PdmObjectType.Assembly && parent.CurrentVersionId != null
         join version in context.Versions.AsNoTracking()
             on new
             {
                 CurrentVersionId = parent.CurrentVersionId,
                 ObjectId = parent.Id
             }
             equals new
             {
                 CurrentVersionId = (Guid?)version.Id,
                 version.ObjectId
             }
         where version.State != VersionState.Cancelled
         join link in context.BomLinks.AsNoTracking() on version.Id equals link.ParentVersionId
         select new CompositionGraphEdge(parent.Id, link.ChildObjectId))
        .ToListAsync(ct);
}
