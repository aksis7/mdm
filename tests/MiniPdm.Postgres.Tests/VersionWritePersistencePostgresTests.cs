using Microsoft.EntityFrameworkCore;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Domain.Versions.Mutations;
using MiniPdm.Storage;
using MiniPdm.Modules.Import.Abstractions.Database;
using MiniPdm.Modules.Import.DtoModels.Database;
using MiniPdm.Modules.Import.Services.Database;
using MiniPdm.Modules.Versions.DtoModels;
using MiniPdm.Modules.Versions.Services;
using MiniPdm.Storage.Concurrency;

using Npgsql;
using Xunit;

namespace MiniPdm.Postgres.Tests;

/// <summary>
/// Проверяет сохранение версий и сериализацию связанных изменений в PostgreSQL.
/// </summary>
public sealed class VersionMutationServicePostgresTests
{
    private const string ConnectionVariable = "PDM_TEST_POSTGRES_CONNECTION";

    /// <summary>
    /// Проверяет, что параллельные запросы клонирования с одним токеном создают ровно одну новую версию.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [Fact]
    public async Task Concurrent_clones_with_same_token_create_exactly_one_version()
    {
        var options = Options();
        var item = new PdmObject { Type = PdmObjectType.Part, Designation = Designation() };
        var version = new ObjectVersion { ObjectId = item.Id, Version = 1, Name = "Part", Mass = 1m };

        try
        {
            await SeedAsync(options, item, version);
            var token = await ReadTokenAsync(options, item.Id);
            async Task<VersionMutationResult> CloneAsync()
            {
                await using var context = new PdmDbContext(options);
                return await new VersionMutationService(context).ExecuteAsync(
                    new VersionWriteRequestDto
                    {
                        ObjectId = item.Id,
                        VersionNumber = 1,
                        ExpectedConcurrencyToken = token,
                        ReferencedChildIds = []
                    }, VersionMutationPlanner.Clone, CancellationToken.None);
            }

            var results = await Task.WhenAll(Task.Run(CloneAsync), Task.Run(CloneAsync));

            Assert.Single(results, x => x.Status == VersionMutationStatus.Succeeded);
            Assert.Single(results, x => x.Status == VersionMutationStatus.Conflict);
            await using var verify = new PdmDbContext(options);
            Assert.Equal(2, await verify.Versions.CountAsync(x => x.ObjectId == item.Id));
            Assert.Equal(2, await verify.Objects.Where(x => x.Id == item.Id)
                .Join(verify.Versions, o => o.CurrentVersionId, v => (Guid?)v.Id, (_, v) => v.Version).SingleAsync());
        }
        finally
        {
            await CleanupAsync(options, [item.Id]);
        }
    }

    /// <summary>
    /// Проверяет сериализацию параллельных изменений состава разных объектов при проверке циклов.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [Fact]
    public async Task Concurrent_cross_object_composition_writes_serialize_cycle_validation()
    {
        var options = Options();
        var first = new PdmObject { Type = PdmObjectType.Assembly, Designation = Designation() };
        var second = new PdmObject { Type = PdmObjectType.Assembly, Designation = Designation() };
        var firstVersion = new ObjectVersion { ObjectId = first.Id, Version = 1, Name = "First" };
        var secondVersion = new ObjectVersion { ObjectId = second.Id, Version = 1, Name = "Second" };

        try
        {
            await SeedAsync(options, first, firstVersion, second, secondVersion);
            var firstToken = await ReadTokenAsync(options, first.Id);
            var secondToken = await ReadTokenAsync(options, second.Id);
            async Task<VersionMutationResult> ComposeAsync(PdmObject parent, ObjectVersion version, Guid token, Guid childId)
            {
                await using var context = new PdmDbContext(options);
                return await new VersionMutationService(context).ExecuteAsync(
                    new VersionWriteRequestDto
                    {
                        ObjectId = parent.Id,
                        VersionNumber = version.Version,
                        ExpectedConcurrencyToken = token,
                        ReferencedChildIds = [childId]
                    },
                    snapshot => VersionMutationPlanner.ReplaceComposition(snapshot, [new CompositionItem(childId, 1)]),
                    CancellationToken.None);
            }

            var results = await Task.WhenAll(
                Task.Run(() => ComposeAsync(first, firstVersion, firstToken, second.Id)),
                Task.Run(() => ComposeAsync(second, secondVersion, secondToken, first.Id)));

            Assert.Single(results, x => x.Status == VersionMutationStatus.Succeeded);
            var rejected = Assert.Single(results, x => x.Status == VersionMutationStatus.Conflict);
            Assert.Equal("Cycle", rejected.Error?.Code);
            await using var verify = new PdmDbContext(options);
            Assert.Equal(1, await verify.BomLinks.CountAsync(x => x.ParentVersionId == firstVersion.Id || x.ParentVersionId == secondVersion.Id));
        }
        finally
        {
            await CleanupAsync(options, [first.Id, second.Id]);
        }
    }

    /// <summary>
    /// Проверяет, что импорт и изменение версии ожидают одну блокировку графа и продолжаются после её освобождения.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [Fact]
    public async Task Import_and_version_writes_wait_on_the_same_graph_lock()
    {
        var options = Options();
        var item = new PdmObject { Type = PdmObjectType.Part, Designation = Designation() };
        var version = new ObjectVersion { ObjectId = item.Id, Version = 1, Name = "Part", Mass = 1m };
        var importId = Guid.NewGuid();
        await using var locker = new PdmDbContext(options);
        await using var lockTransaction = await locker.Database.BeginTransactionAsync();
        Task<ImportPersistenceResultDto>? importTask = null;
        Task<VersionMutationResult>? versionTask = null;
        try
        {
            await SeedAsync(options, item, version);
            await locker.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({GraphWriteLock.AdvisoryLockKey})");
            var token = await ReadTokenAsync(options, item.Id);
            var importEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var versionEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var importWaitingPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var versionWaitingPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<ImportPersistenceResultDto> RunImportAsync()
            {
                await using var context = new PdmDbContext(options);
                await context.Database.OpenConnectionAsync();
                importWaitingPid.TrySetResult(((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID);
                var factory = new TestContextFactory(options);
                var persistence = new ImportDatabaseService(context, factory);
                return await persistence.ExecuteAsync(importId, new ImportLookupDto
                {
                    Designations = [],
                    NormalizedStandardNames = []
                }, (_, _) =>
                {
                    importEntered.TrySetResult(true);
                    return Task.FromResult(new ImportWritePlanDto
                    {
                        NewObjects = [],
                        NewVersions = [],
                        CurrentVersions = [],
                        ReportJson = "{}"
                    });
                }, CancellationToken.None);
            }

            async Task<VersionMutationResult> RunVersionWriteAsync()
            {
                await using var context = new PdmDbContext(options);
                await context.Database.OpenConnectionAsync();
                versionWaitingPid.TrySetResult(((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID);
                return await new VersionMutationService(context).ExecuteAsync(
                    new VersionWriteRequestDto
                    {
                        ObjectId = item.Id,
                        VersionNumber = 1,
                        ExpectedConcurrencyToken = token,
                        ReferencedChildIds = []
                    }, snapshot =>
                    {
                        versionEntered.TrySetResult(true);
                        return VersionMutationPlanner.Clone(snapshot);
                    }, CancellationToken.None);
            }

            var startedImport = Task.Run(RunImportAsync);
            var startedVersionWrite = Task.Run(RunVersionWriteAsync);
            importTask = startedImport;
            versionTask = startedVersionWrite;
            var importPid = await importWaitingPid.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var versionPid = await versionWaitingPid.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForLockWaitersAsync(options, importPid, versionPid);
            Assert.False(importEntered.Task.IsCompleted);
            Assert.False(versionEntered.Task.IsCompleted);

            await lockTransaction.CommitAsync();
            await Task.WhenAll(startedImport, startedVersionWrite);
            Assert.Equal(ImportCommitState.Completed, (await startedImport).State);
            Assert.Equal(VersionMutationStatus.Succeeded, (await startedVersionWrite).Status);
            Assert.True(importEntered.Task.IsCompletedSuccessfully);
            Assert.True(versionEntered.Task.IsCompletedSuccessfully);
        }
        finally
        {
            try
            {
                await lockTransaction.RollbackAsync();
            }
            catch { }
            if (importTask is not null && versionTask is not null)
            {
                try
                {
                    await Task.WhenAll(importTask, versionTask);
                }
                catch { }
            }
            await CleanupAsync(options, [item.Id]);
            await using var cleanup = new PdmDbContext(options);
            var journal = await cleanup.ImportJournals.SingleOrDefaultAsync(x => x.ImportId == importId);
            if (journal is not null)
            {
                cleanup.ImportJournals.Remove(journal);
                await cleanup.SaveChangesAsync();
            }
        }
    }

    /// <summary>
    /// Создаёт параметры подключения контекста к выделенной тестовой базе данных.
    /// </summary>
    /// <returns>Параметры контекста, настроенного для PostgreSQL-тестов.</returns>
    private static DbContextOptions<PdmDbContext> Options()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Set {ConnectionVariable} to a dedicated, already-migrated PostgreSQL test database before running these integration tests.");
        return new DbContextOptionsBuilder<PdmDbContext>().UseNpgsql(connectionString).Options;
    }

    /// <summary>
    /// Сохраняет тестовые объекты и версии в базе данных.
    /// </summary>
    /// <param name="options">Параметры подключения к тестовой базе данных.</param>
    /// <param name="entities">Сущности, которые нужно сохранить для теста.</param>
    /// <returns>Задача завершается после сохранения тестовых объектов и версий.</returns>
    private static async Task SeedAsync(DbContextOptions<PdmDbContext> options, params object[] entities)
    {
        await using var context = new PdmDbContext(options);
        var objects = entities.OfType<PdmObject>().ToArray();
        var versions = entities.OfType<ObjectVersion>().ToArray();
        context.Objects.AddRange(objects);
        await context.SaveChangesAsync();
        context.Versions.AddRange(versions);
        await context.SaveChangesAsync();
        foreach (var obj in objects)
            obj.CurrentVersionId = versions.Single(v => v.ObjectId == obj.Id).Id;
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Читает токен конкурентного доступа объекта.
    /// </summary>
    /// <param name="options">Параметры подключения к тестовой базе данных.</param>
    /// <param name="id">Идентификатор объекта для чтения.</param>
    /// <returns>Асинхронный результат операции и данные, полученные в результате её выполнения.</returns>
    private static async Task<Guid> ReadTokenAsync(DbContextOptions<PdmDbContext> options, Guid id)
    {
        await using var context = new PdmDbContext(options);
        return await context.Objects.Where(x => x.Id == id).Select(x => x.ConcurrencyToken).SingleAsync();
    }

    /// <summary>
    /// Ожидает появления заданных процессов среди ожидающих общую блокировку графа.
    /// </summary>
    /// <param name="options">Параметры подключения к тестовой базе данных.</param>
    /// <param name="firstPid">Идентификатор первого процесса, ожидающего блокировку.</param>
    /// <param name="secondPid">Идентификатор второго процесса, ожидающего блокировку.</param>
    /// <returns>Завершение асинхронной операции.</returns>
    private static async Task WaitForLockWaitersAsync(DbContextOptions<PdmDbContext> options, int firstPid, int secondPid)
    {
        var key = unchecked((ulong)GraphWriteLock.AdvisoryLockKey);
        var high = (uint)(key >> 32);
        var low = (uint)key;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            await using var context = new PdmDbContext(options);
            var waiting = await context.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value"
                FROM pg_locks
                WHERE locktype = 'advisory'
                  AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
                  AND classid = {high}::oid
                  AND objid = {low}::oid
                  AND objsubid = 1
                  AND pid IN ({firstPid}, {secondPid})
                  AND granted = false
                """).SingleAsync();
            if (waiting == 2)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
        throw new TimeoutException("Import and version writes did not both wait on the shared PostgreSQL graph lock.");
    }

    /// <summary>
    /// Удаляет созданные тестом объекты и связанные записи.
    /// </summary>
    /// <param name="options">Параметры подключения к тестовой базе данных.</param>
    /// <param name="objectIds">Идентификаторы объектов, созданных тестом.</param>
    /// <returns>Завершение асинхронной операции.</returns>
    private static async Task CleanupAsync(DbContextOptions<PdmDbContext> options, Guid[] objectIds)
    {
        await using var context = new PdmDbContext(options);
        var items = await context.Objects.Where(x => objectIds.Contains(x.Id)).ToListAsync();
        foreach (var item in items)
            item.CurrentVersionId = null;
        await context.SaveChangesAsync();
        var versions = await context.Versions.Where(x => objectIds.Contains(x.ObjectId)).ToListAsync();
        var versionIds = versions.Select(x => x.Id).ToArray();
        context.BomLinks.RemoveRange(await context.BomLinks.Where(x => versionIds.Contains(x.ParentVersionId)).ToListAsync());
        await context.SaveChangesAsync();
        context.Versions.RemoveRange(versions);
        await context.SaveChangesAsync();
        context.Objects.RemoveRange(items);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Создаёт уникальное обозначение для тестовых данных.
    /// </summary>
    /// <returns>Уникальное обозначение объекта в тестовом формате.</returns>
    private static string Designation() => $"АБВГ.30{Random.Shared.Next(1000, 9999):0000}.{Random.Shared.Next(0, 999):000}";

    private sealed class TestContextFactory(DbContextOptions<PdmDbContext> options) : IDbContextFactory<PdmDbContext>
    {
        /// <summary>
        /// Создаёт контекст базы данных с настроенными параметрами.
        /// </summary>
        /// <returns>Новый контекст базы данных PDM.</returns>
        public PdmDbContext CreateDbContext() => new(options);
        /// <summary>
        /// Асинхронно создаёт контекст базы данных с настроенными параметрами.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
        /// <returns>Задача с новым контекстом базы данных PDM.</returns>
        public Task<PdmDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
