using MiniPdm.Common.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MiniPdm.Domain.Composition;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Storage;
using MiniPdm.Modules.Import.Abstractions.Database;
using MiniPdm.Modules.Import.DtoModels.Database;
using MiniPdm.Modules.Import.Services.Database;
using Xunit;

namespace MiniPdm.Storage.Tests;

/// <summary>
/// Проверяет атомарную запись импорта, журналирование, откат и согласование текущих версий.
/// </summary>
public sealed class ImportPersistenceTests
{
    /// <summary>
    /// Проверяет атомарную запись новых объектов, версий, текущих указателей и журнала импорта.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Writes_new_objects_versions_current_pointer_and_journal_atomically()
    {
        await using var fixture = await Fixture.CreateAsync();
        var importId = Guid.NewGuid();
        var newObject = new PdmObject { Type = PdmObjectType.Part, Designation = "БВГД.123456.001" };
        var newVersion = new ObjectVersion { ObjectId = newObject.Id, Version = 1, Name = "Новая" };

        var result = await fixture.Persistence.ExecuteAsync(importId, Lookup(), (snapshot, _) =>
        {
            Assert.Empty(snapshot.ExistingObjects);
            return Task.FromResult(new ImportWritePlanDto
            {
                NewObjects = [newObject],
                NewVersions = [newVersion],
                CurrentVersions = [new CurrentVersionAssignmentDto
                {
                                        Object = newObject,
                                        Version = newVersion
                }],
                ReportJson = "{\"ok\":true}"
            });
        }, CancellationToken.None);

        Assert.Equal(ImportCommitState.Completed, result.State);
        await using var verify = new PdmDbContext(fixture.Options);
        var savedObject = await verify.Objects.SingleAsync(x => x.Id == newObject.Id);
        Assert.Equal(newVersion.Id, savedObject.CurrentVersionId);
        Assert.Equal("Новая", (await verify.Versions.SingleAsync(x => x.Id == newVersion.Id)).Name);
        Assert.Equal("{\"ok\":true}", (await verify.ImportJournals.SingleAsync(x => x.ImportId == importId)).ReportJson);
    }

    /// <summary>
    /// Проверяет повторное использование журнала без повторного вызова этапа подготовки.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Replays_journal_without_invoking_prepare_again()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        await CompleteEmptyImport(fixture.Persistence, id);
        var invoked = false;

        var result = await fixture.Persistence.ExecuteAsync(id, Lookup(), (_, _) =>
        {
            invoked = true;
            throw new InvalidOperationException("must not be called");
        }, CancellationToken.None);

        Assert.False(invoked);
        Assert.Equal(ImportCommitState.Completed, result.State);
        Assert.True(result.Replayed);
        Assert.Equal("{}", result.ReportJson);
    }

    /// <summary>
    /// Проверяет откат неудачной подготовки без сохранения строк и записи журнала.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Failed_prepare_rolls_back_without_rows_or_journal()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        var result = await fixture.Persistence.ExecuteAsync(id, Lookup(), (_, _) => throw new InvalidOperationException("prepare failed"), CancellationToken.None);

        Assert.Equal(ImportCommitState.ConfirmedRollback, result.State);
        await using var verify = new PdmDbContext(fixture.Options);
        Assert.Empty(await verify.Objects.ToListAsync());
        Assert.Empty(await verify.Versions.ToListAsync());
        Assert.Empty(await verify.ImportJournals.ToListAsync());
    }

    /// <summary>
    /// Проверяет, что завершённый импорт не компенсируется.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Completed_import_is_never_compensated()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        await CompleteEmptyImport(fixture.Persistence, id);
        var called = false;

        var result = await fixture.Persistence.CompensateIfRolledBackAsync(id, _ =>
        {
            called = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.False(result);
        Assert.False(called);
    }

    /// <summary>
    /// Проверяет сохранение известной ошибки бизнес-правила после подтверждённого отката.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Confirmed_rollback_rethrows_known_business_logic_error()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<BusinessLogicException>(() => fixture.Persistence.ExecuteAsync(
            id, Lookup(), (_, _) => throw new BusinessLogicException("A cycle would be created."), CancellationToken.None));

        Assert.Equal("A cycle would be created.", exception.Message);
        await using var verify = new PdmDbContext(fixture.Options);
        Assert.Empty(await verify.Objects.ToListAsync());
        Assert.Empty(await verify.ImportJournals.ToListAsync());
    }

    /// <summary>
    /// Проверяет откат изменений callback в существующей текущей версии при неудачном сохранении.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Failed_save_rolls_back_callback_mutations_to_existing_current_version()
    {
        await using var fixture = await Fixture.CreateAsync();
        Guid objectId;
        Guid versionId;
        await using (var seed = new PdmDbContext(fixture.Options))
        {
            var obj = new PdmObject { Type = PdmObjectType.Part, Designation = "АБВГ.301245.001" };
            seed.Objects.Add(obj);
            await seed.SaveChangesAsync();
            var version = new ObjectVersion { ObjectId = obj.Id, Version = 1, Name = "Before" };
            seed.Versions.Add(version);
            await seed.SaveChangesAsync();
            obj.CurrentVersionId = version.Id;
            await seed.SaveChangesAsync();
            objectId = obj.Id;
            versionId = version.Id;
        }

        var importId = Guid.NewGuid();
        var result = await fixture.Persistence.ExecuteAsync(importId, Lookup("АБВГ.301245.001"), (snapshot, _) =>
        {
            var existing = Assert.Single(snapshot.ExistingObjects);
            existing.CurrentVersion!.Name = "Changed by callback";
            var invalid = new ObjectVersion { ObjectId = objectId, Version = 2, Mass = -1m };
            return Task.FromResult(new ImportWritePlanDto
            {
                NewObjects = [],
                NewVersions = [invalid],
                CurrentVersions = [],
                ReportJson = "{}"
            });
        }, CancellationToken.None);

        Assert.Equal(ImportCommitState.ConfirmedRollback, result.State);
        await using var verify = new PdmDbContext(fixture.Options);
        Assert.Equal("Before", (await verify.Versions.SingleAsync(x => x.Id == versionId)).Name);
        Assert.Equal(1, await verify.Versions.CountAsync());
        Assert.Empty(await verify.ImportJournals.ToListAsync());
    }

    /// <summary>
    /// Проверяет удаление старых связей состава до переключения текущей версии.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Removes_old_bom_links_before_advancing_current_version()
    {
        await using var fixture = await Fixture.CreateAsync();
        Guid parentId;
        Guid oldVersionId;
        Guid childId;
        await using (var seed = new PdmDbContext(fixture.Options))
        {
            var parent = new PdmObject { Type = PdmObjectType.Assembly, Designation = "АБВГ.301245.001" };
            var child = new PdmObject { Type = PdmObjectType.Part, Designation = "ДЕЖЗ.301245.001" };
            seed.Objects.AddRange(parent, child);
            await seed.SaveChangesAsync();
            var parentVersion = new ObjectVersion { ObjectId = parent.Id, Version = 1 };
            var childVersion = new ObjectVersion { ObjectId = child.Id, Version = 1 };
            seed.Versions.AddRange(parentVersion, childVersion);
            await seed.SaveChangesAsync();
            parent.CurrentVersionId = parentVersion.Id;
            child.CurrentVersionId = childVersion.Id;
            seed.BomLinks.Add(new BomLink { ParentVersionId = parentVersion.Id, ChildObjectId = child.Id, Quantity = 1 });
            await seed.SaveChangesAsync();
            parentId = parent.Id;
            oldVersionId = parentVersion.Id;
            childId = child.Id;
        }

        var result = await fixture.Persistence.ExecuteAsync(Guid.NewGuid(), Lookup("АБВГ.301245.001"), (snapshot, _) =>
        {
            Assert.Contains(snapshot.CurrentGraph, edge => edge.ParentId == parentId && edge.ChildId == childId);
            var parent = Assert.Single(snapshot.ExistingObjects);
            var oldLink = Assert.Single(parent.CurrentVersion!.Components);
            var replacement = new ObjectVersion { ObjectId = parentId, Version = 2 };
            return Task.FromResult(new ImportWritePlanDto
            {
                NewObjects = [],
                NewVersions = [replacement],
                CurrentVersions = [new CurrentVersionAssignmentDto
                {
                                        Object = parent,
                                        Version = replacement
                }],
                ReportJson = "{}",
                RemovedLinks = [oldLink]
            });
        }, CancellationToken.None);

        Assert.Equal(ImportCommitState.Completed, result.State);
        await using var verify = new PdmDbContext(fixture.Options);
        Assert.Empty(await verify.BomLinks.ToListAsync());
        var parentAfter = await verify.Objects.SingleAsync(x => x.Id == parentId);
        Assert.Equal(2, await verify.Versions.CountAsync(x => x.ObjectId == parentId));
        Assert.Equal(2, await verify.Versions.Where(x => x.Id == parentAfter.CurrentVersionId).Select(x => x.Version).SingleAsync());
        Assert.True(await verify.Versions.AnyAsync(x => x.Id == oldVersionId));
    }

    private static ImportLookupDto Lookup(string designation = "АБВГ.301245.001") => new ImportLookupDto
    {
        Designations = [designation],
        NormalizedStandardNames = []
    };

    private static Task<ImportPersistenceResultDto> CompleteEmptyImport(IImportDatabaseService persistence, Guid id) =>
        persistence.ExecuteAsync(id, Lookup(), (_, _) => Task.FromResult(new ImportWritePlanDto
        {
            NewObjects = [],
            NewVersions = [],
            CurrentVersions = [],
            ReportJson = "{}"
        }), CancellationToken.None);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        /// <summary>
        /// Параметры SQLite-контекста базы данных тестовой фикстуры.
        /// </summary>
        public DbContextOptions<PdmDbContext> Options
        {
            get;
        }
        /// <summary>
        /// Контекст базы данных для проверки операций импорта.
        /// </summary>
        private PdmDbContext Context
        {
            get;
        }
        /// <summary>
        /// Сервис сохранения импорта, связанный с контекстом фикстуры.
        /// </summary>
        public ImportDatabaseService Persistence
        {
            get;
        }

        private Fixture(SqliteConnection connection, DbContextOptions<PdmDbContext> options)
        {
            _connection = connection;
            Options = options;
            Context = new PdmDbContext(options);
            Persistence = new ImportDatabaseService(Context, new TestContextFactory(options));
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<PdmDbContext>().UseSqlite(connection).Options;
            await using (var db = new PdmDbContext(options))
                await db.Database.EnsureCreatedAsync();
            return new Fixture(connection, options);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestContextFactory(DbContextOptions<PdmDbContext> options) : IDbContextFactory<PdmDbContext>
    {
        public PdmDbContext CreateDbContext() => new(options);
        public Task<PdmDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
