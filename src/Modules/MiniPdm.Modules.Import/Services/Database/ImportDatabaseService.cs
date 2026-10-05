using MiniPdm.Common.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MiniPdm.Modules.Import.Abstractions.Database;
using MiniPdm.Modules.Import.DtoModels.Database;
using MiniPdm.Domain.Composition;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Storage.Entities;
using MiniPdm.Storage.Concurrency;
using MiniPdm.Storage;
using System.Data;

namespace MiniPdm.Modules.Import.Services.Database;

/// <summary>
/// Сохраняет пакет и журнал идемпотентности в общей транзакции базы данных.
/// </summary>
/// <param name="context">Контекст для транзакционной записи импорта.</param>
/// <param name="contextFactory">Фабрика независимых контекстов для чтения результата и восстановления.</param>
public sealed class ImportDatabaseService(PdmDbContext context, IDbContextFactory<PdmDbContext> contextFactory) : IImportDatabaseService
{
    /// <inheritdoc />
    public async Task<ImportPersistenceResultDto> ExecuteAsync(Guid importId, ImportLookupDto lookup, Func<ImportSnapshotDto, CancellationToken, Task<ImportWritePlanDto>> prepare, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await using var transaction = await context.Database.BeginTransactionAsync(TransactionIsolation(context), ct);
        var committed = false;
        var commitAttempted = false;
        try
        {
            await AcquireLockAsync(context, transaction, ct);
            var prior = await context.ImportJournals.AsNoTracking().SingleOrDefaultAsync(x => x.ImportId == importId, ct);
            if (prior is not null)
            {
                commitAttempted = true;
                await transaction.CommitAsync(CancellationToken.None);
                committed = true;
                return new ImportPersistenceResultDto
                {
                    State = ImportCommitState.Completed,
                    Replayed = true,
                    ReportJson = prior.ReportJson
                };
            }

            var designations = lookup.Designations.Distinct().ToArray();
            var names = lookup.NormalizedStandardNames.Distinct().ToArray();
            var objects = await context.Objects
                .Where(x => x.Designation != null && designations.Contains(x.Designation) || x.NormalizedName != null && names.Contains(x.NormalizedName))
                .Include(x => x.Versions)
                .Include(x => x.CurrentVersion!).ThenInclude(v => v.Components)
                .AsSplitQuery()
                .ToListAsync(ct);
            // Flat active-edge projection for cycle checks; tree retrieval remains a separate recursive CTE query.
            var graph = (await ActiveCompositionGraphQuery.LoadAsync(context, ct))
                .Select(edge => new ActiveGraphEdgeDto
                {
                    ParentId = edge.ParentId,
                    ChildId = edge.ChildId
                }).ToArray();
            var plan = await prepare(new ImportSnapshotDto
            {
                ExistingObjects = objects,
                CurrentGraph = graph
            }, ct);

            context.Objects.AddRange(plan.NewObjects);
            if (plan.RemovedLinks is { Count: > 0 })
                context.BomLinks.RemoveRange(plan.RemovedLinks);
            await context.SaveChangesAsync(CancellationToken.None);
            context.Versions.AddRange(plan.NewVersions);
            await context.SaveChangesAsync(CancellationToken.None);
            foreach (var assignment in plan.CurrentVersions)
                assignment.Object.CurrentVersionId = assignment.Version.Id;
            context.ImportJournals.Add(new ImportJournal { ImportId = importId, CompletedAt = DateTimeOffset.UtcNow, ReportJson = plan.ReportJson });
            await context.SaveChangesAsync(CancellationToken.None);
            commitAttempted = true;
            await transaction.CommitAsync(CancellationToken.None);
            committed = true;
            return new ImportPersistenceResultDto
            {
                State = ImportCommitState.Completed,
                Replayed = false,
                ReportJson = plan.ReportJson
            };
        }
        catch (OperationCanceledException)
        {
            await RollbackAsync(transaction);
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception ex)
        {
            var rollbackSucceeded = false;
            if (!committed)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    rollbackSucceeded = true;
                }
                catch { }
            }
            context.ChangeTracker.Clear();
            if (commitAttempted)
            {
                try
                {
                    await transaction.DisposeAsync();
                }
                catch { }
                var resolution = await ResolveAsync(importId, CancellationToken.None);
                return resolution with
                {
                    Error = resolution.Error is null ? ex.Message : $"{ex.Message}; resolution failed: {resolution.Error}"
                };
            }
            if (rollbackSucceeded && ex is (BusinessLogicException or InputLogicException))
                throw;
            if (rollbackSucceeded)
                return new ImportPersistenceResultDto
                {
                    State = ImportCommitState.ConfirmedRollback,
                    Replayed = false,
                    ReportJson = null,
                    Error = ex.Message
                };
            return new ImportPersistenceResultDto
            {
                State = ImportCommitState.Unknown,
                Replayed = false,
                ReportJson = null,
                Error = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public async Task<ImportPersistenceResultDto?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var fresh = await contextFactory.CreateDbContextAsync(ct);
        var row = await fresh.ImportJournals.AsNoTracking().SingleOrDefaultAsync(x => x.ImportId == id, ct);
        return row is null ? null : new ImportPersistenceResultDto
        {
            State = ImportCommitState.Completed,
            Replayed = true,
            ReportJson = row.ReportJson
        };
    }

    /// <inheritdoc />
    public async Task<ImportPersistenceResultDto> ResolveAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await using var fresh = await contextFactory.CreateDbContextAsync(ct);
            await using var transaction = await fresh.Database.BeginTransactionAsync(TransactionIsolation(fresh), ct);
            await AcquireLockAsync(fresh, transaction, ct);
            var row = await fresh.ImportJournals.AsNoTracking().SingleOrDefaultAsync(x => x.ImportId == id, ct);
            await transaction.CommitAsync(CancellationToken.None);
            return row is null
                ? new ImportPersistenceResultDto
                {
                    State = ImportCommitState.ConfirmedRollback,
                    Replayed = false,
                    ReportJson = null
                }
                : new ImportPersistenceResultDto
                {
                    State = ImportCommitState.Completed,
                    Replayed = true,
                    ReportJson = row.ReportJson
                };
        }
        catch (Exception ex)
        {
            return new ImportPersistenceResultDto
            {
                State = ImportCommitState.Unknown,
                Replayed = false,
                ReportJson = null,
                Error = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public async Task<bool> CompensateIfRolledBackAsync(Guid id, Func<CancellationToken, Task> compensate, CancellationToken ct)
    {
        await using var fresh = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await fresh.Database.BeginTransactionAsync(TransactionIsolation(fresh), ct);
        try
        {
            await AcquireLockAsync(fresh, transaction, ct);
            var exists = await fresh.ImportJournals.AsNoTracking().AnyAsync(x => x.ImportId == id, ct);
            if (exists)
            {
                await transaction.CommitAsync(CancellationToken.None);
                return false;
            }
            await compensate(ct);
            await transaction.CommitAsync(CancellationToken.None);
            return true;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch { }
            throw;
        }
    }

    private static async Task AcquireLockAsync(PdmDbContext db, IDbContextTransaction transaction, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({GraphWriteLock.AdvisoryLockKey})", ct);
        // SQLite test provider serializes test writes through its transaction/connection; it has no production advisory-lock analogue.
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
}
