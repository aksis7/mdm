using System.Data;
using MiniPdm.Modules.Versions.Abstractions;
using MiniPdm.Modules.Versions.DtoModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MiniPdm.Domain.Composition;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Domain.Versions.Mutations;
using MiniPdm.Storage.Concurrency;
using MiniPdm.Storage;
using Npgsql;

namespace MiniPdm.Modules.Versions.Services;

/// <summary>
/// Сохраняет мутации версий под блокировкой графа и проверкой токена конкурентности.
/// </summary>
/// <param name="context">Контекст базы данных, используемый для транзакционного чтения и записи.</param>
public sealed class VersionMutationService(PdmDbContext context) : IVersionMutationService
{
    /// <inheritdoc />
    public Task<VersionMutationResult> CloneAsync(Guid objectId, int sourceVersion,
        Guid expectedConcurrencyToken, CancellationToken ct) =>
        ExecuteAsync(new VersionWriteRequestDto
        {
            ObjectId = objectId,
            VersionNumber = sourceVersion,
            ExpectedConcurrencyToken = expectedConcurrencyToken,
            ReferencedChildIds = []
        },
            VersionMutationPlanner.Clone, ct);

    /// <inheritdoc />
    public Task<VersionMutationResult> ChangeStateAsync(Guid objectId, int version, VersionState state,
        Guid expectedConcurrencyToken, CancellationToken ct) =>
        ExecuteAsync(new VersionWriteRequestDto
        {
            ObjectId = objectId,
            VersionNumber = version,
            ExpectedConcurrencyToken = expectedConcurrencyToken,
            ReferencedChildIds = []
        },
            snapshot => VersionMutationPlanner.ChangeState(snapshot, state), ct);

    /// <inheritdoc />
    public Task<VersionMutationResult> UpdateAttributesAsync(Guid objectId, int version, string? name,
        string? material, decimal? mass, Guid expectedConcurrencyToken, CancellationToken ct) =>
        ExecuteAsync(new VersionWriteRequestDto
        {
            ObjectId = objectId,
            VersionNumber = version,
            ExpectedConcurrencyToken = expectedConcurrencyToken,
            ReferencedChildIds = []
        },
            snapshot => VersionMutationPlanner.UpdateAttributes(snapshot, name, material, mass), ct);

    /// <inheritdoc />
    public Task<VersionMutationResult> ReplaceCompositionAsync(Guid objectId, int version,
        IReadOnlyList<CompositionItem> components, Guid expectedConcurrencyToken, CancellationToken ct) =>
        ExecuteAsync(new VersionWriteRequestDto
        {
            ObjectId = objectId,
            VersionNumber = version,
            ExpectedConcurrencyToken = expectedConcurrencyToken,
            ReferencedChildIds = components.Select(x => x.ChildObjectId).Distinct().ToArray()
        },
            snapshot => VersionMutationPlanner.ReplaceComposition(snapshot, components), ct);

    /// <summary>
    /// Подготавливает и атомарно применяет план мутации внутри транзакции.
    /// </summary>
    /// <param name="request">Идентификатор объекта, версии и предусловия записи.</param>
    /// <param name="prepare">Функция построения плана по загруженному снимку.</param>
    /// <param name="ct">Токен отмены до начала записи.</param>
    /// <returns>Результат мутации либо статус отказа в проверке предусловий.</returns>
    public async Task<VersionMutationResult> ExecuteAsync(
        VersionWriteRequestDto request,
        Func<VersionMutationSnapshot, VersionMutationPlan> prepare,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(prepare);
        ct.ThrowIfCancellationRequested();

        // A scoped context can have been used before this command. Never let its identity map
        // supply the object or graph snapshot protected by the transaction lock.
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(TransactionIsolation(context), ct);
        var commitAttempted = false;
        try
        {
            await AcquireLockAsync(context, ct);
            context.ChangeTracker.Clear();

            var item = await context.Objects.SingleOrDefaultAsync(x => x.Id == request.ObjectId, ct);
            if (item is null)
            {
                await RollbackAsync(transaction);
                context.ChangeTracker.Clear();
                return Failure(VersionMutationStatus.NotFound, request.ObjectId, "ObjectNotFound", "The object was not found.");
            }

            if (item.ConcurrencyToken != request.ExpectedConcurrencyToken)
            {
                var staleResult = Failure(VersionMutationStatus.Conflict, item.Id, "ConcurrencyConflict", "The object has changed since it was read.") with
                {
                    CurrentVersionId = item.CurrentVersionId,
                    ConcurrencyToken = item.ConcurrencyToken
                };
                await RollbackAsync(transaction);
                context.ChangeTracker.Clear();
                return staleResult;
            }

            var versions = await context.Versions
                .Where(x => x.ObjectId == item.Id)
                .ToListAsync(ct);
            item.Versions = versions;
            item.CurrentVersion = versions.SingleOrDefault(x => x.Id == item.CurrentVersionId);
            var selected = versions.SingleOrDefault(x => x.Version == request.VersionNumber);
            if (selected is null)
            {
                await RollbackAsync(transaction);
                context.ChangeTracker.Clear();
                return Failure(VersionMutationStatus.NotFound, request.ObjectId, "VersionNotFound", "The requested version was not found.") with
                {
                    CurrentVersionId = item.CurrentVersionId,
                    ConcurrencyToken = item.ConcurrencyToken
                };
            }

            var neededVersionIds = versions
                .Where(x => x.Id == selected.Id || x.Id == item.CurrentVersionId)
                .Concat(versions.Where(x => x.State != VersionState.Cancelled).OrderByDescending(x => x.Version).Take(2))
                .Select(x => x.Id)
                .Distinct()
                .ToArray();
            if (neededVersionIds.Length > 0)
                await context.BomLinks.Where(x => neededVersionIds.Contains(x.ParentVersionId)).LoadAsync(ct);

            var graph = await ActiveCompositionGraphQuery.LoadAsync(context, ct);

            var referencedIds = request.ReferencedChildIds.Distinct().ToArray();
            var existingChildIds = referencedIds.Length == 0
                ? new HashSet<Guid>()
                : (await context.Objects.AsNoTracking()
                    .Where(x => referencedIds.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToListAsync(ct)).ToHashSet();

            ct.ThrowIfCancellationRequested();
            var plan = prepare(new VersionMutationSnapshot(item, selected, graph, existingChildIds));
            ct.ThrowIfCancellationRequested();
            if (plan.Status != VersionMutationStatus.Succeeded)
            {
                await RollbackAsync(transaction);
                context.ChangeTracker.Clear();
                return ResultFromPlan(request.ObjectId, item, plan);
            }

            if (plan.RemovedLinks.Count > 0)
                context.BomLinks.RemoveRange(plan.RemovedLinks);
            if (plan.NewVersion is not null)
                context.Versions.Add(plan.NewVersion);

            // Persist the new version and its links before assigning the composite-FK pointer.
            // Once writes begin, cancellation cannot interrupt save/commit in an ambiguous state.
            await context.SaveChangesAsync(CancellationToken.None);
            item.CurrentVersionId = plan.DesiredCurrentVersionId;
            await context.SaveChangesAsync(CancellationToken.None);

            var versionForResult = plan.NewVersion ?? plan.Version;
            var result = new VersionMutationResult(
                VersionMutationStatus.Succeeded,
                request.ObjectId,
                versionForResult?.Id,
                versionForResult?.Version,
                versionForResult?.State,
                item.CurrentVersionId,
                item.ConcurrencyToken,
                null,
                plan.Warnings);

            commitAttempted = true;
            try
            {
                await transaction.CommitAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                context.ChangeTracker.Clear();
                throw new VersionWriteUncertainException("The version write commit outcome is unknown.", ex);
            }

            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (commitAttempted)
                throw;
            await RollbackAsync(transaction);
            context.ChangeTracker.Clear();
            return Failure(VersionMutationStatus.Conflict, request.ObjectId, "ConcurrencyConflict", "The object changed while the version mutation was being saved.");
        }
        catch (DbUpdateException ex) when (IsExpectedConstraintConflict(ex) && !commitAttempted)
        {
            await RollbackAsync(transaction);
            context.ChangeTracker.Clear();
            return Failure(VersionMutationStatus.Conflict, request.ObjectId, "PersistenceConflict", "The version mutation conflicts with a persisted constraint.");
        }
        catch
        {
            if (!commitAttempted)
                await RollbackAsync(transaction);
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private static VersionMutationResult ResultFromPlan(Guid objectId, PdmObject item, VersionMutationPlan plan)
    {
        var version = plan.NewVersion ?? plan.Version;
        return new(plan.Status, objectId, version?.Id, version?.Version, version?.State,
            item.CurrentVersionId, item.ConcurrencyToken, plan.Error, plan.Warnings);
    }

    private static VersionMutationResult Failure(VersionMutationStatus status, Guid objectId, string code, string message) =>
        new(status, objectId, null, null, null, null, null, new VersionMutationError(code, message), []);

    private static async Task AcquireLockAsync(PdmDbContext db, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({GraphWriteLock.AdvisoryLockKey})", ct);
    }

    private static IsolationLevel TransactionIsolation(PdmDbContext db) =>
        db.Database.IsNpgsql() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable;

    private static async Task RollbackAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch { }
    }

    private static bool IsExpectedConstraintConflict(DbUpdateException exception)
    {
        if (exception.InnerException is PostgresException postgres)
            return postgres.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation
                or PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ExclusionViolation;
        return false;
    }
}
