using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Domain.Versions.Mutations;
using MiniPdm.Storage;
using MiniPdm.Modules.Versions.DtoModels;
using MiniPdm.Modules.Versions.Abstractions;
using MiniPdm.Modules.Versions.Services;

using Xunit;

namespace MiniPdm.Storage.Tests;

/// <summary>
/// Проверяет сохранение изменений состава через сервис управления версиями.
/// </summary>
public sealed class VersionMutationServiceTests
{
    /// <summary>
    /// Проверяет сохранение следующей версии и атомарное перемещение указателя текущей версии.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Clone_persists_next_version_and_moves_current_pointer_atomically()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (item, first) = await fixture.SeedPartAsync(current: true);

        var result = await fixture.Persistence.ExecuteAsync(Request(item, first), snapshot => VersionMutationPlanner.Clone(snapshot), CancellationToken.None);

        Assert.Equal(VersionMutationStatus.Succeeded, result.Status);
        Assert.Equal(2, result.VersionNumber);
        Assert.Equal(VersionState.InWork, result.State);
        Assert.Equal(result.VersionId, result.CurrentVersionId);
        Assert.NotEqual(item.ConcurrencyToken, result.ConcurrencyToken);
        await using var verify = new PdmDbContext(fixture.Options);
        Assert.Equal(2, await verify.Versions.CountAsync(x => x.ObjectId == item.Id));
        Assert.Equal(result.VersionId, await verify.Objects.Where(x => x.Id == item.Id).Select(x => x.CurrentVersionId).SingleAsync());
    }

    /// <summary>
    /// Проверяет возврат конфликта по устаревшему токену до вызова доменного callback.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Stale_token_returns_conflict_without_invoking_domain_callback()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (item, first) = await fixture.SeedPartAsync(current: true);
        var invoked = false;

        var result = await fixture.Persistence.ExecuteAsync(
            new VersionWriteRequestDto
            {
                ObjectId = item.Id,
                VersionNumber = first.Version,
                ExpectedConcurrencyToken = Guid.NewGuid(),
                ReferencedChildIds = []
            },
            _ => { invoked = true; return VersionMutationPlanner.Clone(null!); },
            CancellationToken.None);

        Assert.False(invoked);
        Assert.Equal(VersionMutationStatus.Conflict, result.Status);
        Assert.Equal("ConcurrencyConflict", result.Error?.Code);
        Assert.Equal(first.Id, result.CurrentVersionId);
        Assert.Equal(item.ConcurrencyToken, result.ConcurrencyToken);
        await using var verify = new PdmDbContext(fixture.Options);
        Assert.Single(await verify.Versions.ToListAsync());
    }

    /// <summary>
    /// Проверяет отбрасывание изменений callback и очистку отслеживания после отклонения плана.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Rejected_plan_discards_callback_mutations_and_clears_tracker()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (item, first) = await fixture.SeedPartAsync(current: true);

        var result = await fixture.Persistence.ExecuteAsync(Request(item, first), snapshot =>
        {
            snapshot.SelectedVersion.Name = "uncommitted callback mutation";
            return new VersionMutationPlan(VersionMutationStatus.Invalid, null, null, null, [],
                new VersionMutationError("Rejected", "Rejected for the test."), []);
        }, CancellationToken.None);

        Assert.Equal(VersionMutationStatus.Invalid, result.Status);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
        await using var verify = new PdmDbContext(fixture.Options);
        Assert.Equal("Part", await verify.Versions.Where(x => x.Id == first.Id).Select(x => x.Name).SingleAsync());
        Assert.Equal(item.ConcurrencyToken, await verify.Objects.Where(x => x.Id == item.Id).Select(x => x.ConcurrencyToken).SingleAsync());
    }

    /// <summary>
    /// Проверяет удаление старых связей до сохранения нового указателя и пакетную загрузку дочерних объектов при изменении состава.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Composition_update_removes_old_links_before_saving_new_pointer_and_bulk_loads_children()
    {
        await using var fixture = await Fixture.CreateAsync();
        var parent = new PdmObject { Type = PdmObjectType.Assembly, Designation = "АБВГ.301245.001" };
        var oldChild = new PdmObject { Type = PdmObjectType.Part, Designation = "ДЕЖЗ.301245.001" };
        var newChild = new PdmObject { Type = PdmObjectType.Part, Designation = "ИЙКЛ.301245.001" };
        var oldParentVersion = new ObjectVersion { ObjectId = parent.Id, Version = 1, Name = "Assembly" };
        var oldChildVersion = new ObjectVersion { ObjectId = oldChild.Id, Version = 1, Name = "Old" };
        var newChildVersion = new ObjectVersion { ObjectId = newChild.Id, Version = 1, Name = "New" };
        oldParentVersion.Components.Add(new MiniPdm.Domain.Composition.BomLink { ParentVersionId = oldParentVersion.Id, ChildObjectId = oldChild.Id, Quantity = 1 });
        fixture.Context.Objects.AddRange(parent, oldChild, newChild);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.Versions.AddRange(oldParentVersion, oldChildVersion, newChildVersion);
        await fixture.Context.SaveChangesAsync();
        parent.CurrentVersionId = oldParentVersion.Id;
        oldChild.CurrentVersionId = oldChildVersion.Id;
        newChild.CurrentVersionId = newChildVersion.Id;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var token = await fixture.Context.Objects.Where(x => x.Id == parent.Id).Select(x => x.ConcurrencyToken).SingleAsync();

        var result = await fixture.Persistence.ExecuteAsync(
            new VersionWriteRequestDto
            {
                ObjectId = parent.Id,
                VersionNumber = 1,
                ExpectedConcurrencyToken = token,
                ReferencedChildIds = [newChild.Id]
            },
            snapshot =>
            {
                Assert.Equal(new HashSet<Guid> { newChild.Id }, snapshot.ExistingChildIds);
                Assert.Contains(snapshot.CurrentGraph, x => x.ParentId == parent.Id && x.ChildId == oldChild.Id);
                return VersionMutationPlanner.ReplaceComposition(snapshot, [new CompositionItem(newChild.Id, 3)]);
            },
            CancellationToken.None);

        Assert.Equal(VersionMutationStatus.Succeeded, result.Status);
        Assert.Equal(oldParentVersion.Id, result.CurrentVersionId);
        await using var verify = new PdmDbContext(fixture.Options);
        Assert.Empty(await verify.BomLinks.Where(x => x.ParentVersionId == oldParentVersion.Id && x.ChildObjectId == oldChild.Id).ToListAsync());
        Assert.Equal(3, await verify.BomLinks.Where(x => x.ParentVersionId == oldParentVersion.Id && x.ChildObjectId == newChild.Id).Select(x => x.Quantity).SingleAsync());
    }

    /// <summary>
    /// Проверяет клонирование объекта с историческими версиями без текущего указателя.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Clone_works_when_object_has_versions_but_no_current_pointer()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (item, first) = await fixture.SeedPartAsync(current: false);

        var result = await fixture.Persistence.ExecuteAsync(Request(item, first), VersionMutationPlanner.Clone, CancellationToken.None);

        Assert.Equal(VersionMutationStatus.Succeeded, result.Status);
        Assert.Equal(result.VersionId, result.CurrentVersionId);
        await using var verify = new PdmDbContext(fixture.Options);
        Assert.Equal(result.VersionId, await verify.Objects.Where(x => x.Id == item.Id).Select(x => x.CurrentVersionId).SingleAsync());
    }

    private static VersionWriteRequestDto Request(PdmObject item, ObjectVersion version) =>
        new VersionWriteRequestDto
        {
            ObjectId = item.Id,
            VersionNumber = version.Version,
            ExpectedConcurrencyToken = item.ConcurrencyToken,
            ReferencedChildIds = []
        };

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
        /// Контекст базы данных, используемый тестовой фикстурой.
        /// </summary>
        public PdmDbContext Context
        {
            get;
        }
        /// <summary>
        /// Сервис сохранения изменений версий, связанный с контекстом фикстуры.
        /// </summary>
        public VersionMutationService Persistence
        {
            get;
        }

        private Fixture(SqliteConnection connection, DbContextOptions<PdmDbContext> options)
        {
            _connection = connection;
            Options = options;
            Context = new PdmDbContext(options);
            Persistence = new VersionMutationService(Context);
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

        public async Task<(PdmObject Item, ObjectVersion Version)> SeedPartAsync(bool current)
        {
            var item = new PdmObject { Type = PdmObjectType.Part, Designation = "АБВГ.301245.001" };
            Context.Objects.Add(item);
            await Context.SaveChangesAsync();
            var version = new ObjectVersion { ObjectId = item.Id, Version = 1, Name = "Part", Mass = 1m };
            Context.Versions.Add(version);
            await Context.SaveChangesAsync();
            if (current)
            {
                item.CurrentVersionId = version.Id;
                await Context.SaveChangesAsync();
            }
            Context.ChangeTracker.Clear();
            item = await Context.Objects.AsNoTracking().SingleAsync(x => x.Id == item.Id);
            version = await Context.Versions.AsNoTracking().SingleAsync(x => x.Id == version.Id);
            return (item, version);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
