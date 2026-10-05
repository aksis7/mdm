using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Storage;
using MiniPdm.Modules.Objects.Services;
using Xunit;

namespace MiniPdm.Storage.Tests;

/// <summary>
/// Проверяет запросы поиска и карточки объекта, включая историю версий.
/// </summary>
public sealed class ObjectReadQueryTests
{
    /// <summary>
    /// Проверяет поиск по буквальным ASCII-подстрокам, текущим версиям и стабильной пагинации без отслеживания.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Search_uses_literal_ascii_substrings_current_versions_and_stable_paging_without_tracking()
    {
        await using var fixture = await Fixture.OpenAsync();
        var a = await fixture.CreateAsync(PdmObjectType.Part, "A_001", "Alpha", VersionState.Approved, 1.25m);
        var b = await fixture.CreateAsync(PdmObjectType.Part, "A_002", "Beta", VersionState.InWork, 2m);
        await fixture.CreateAsync(PdmObjectType.Part, "C_003", "Gamma", VersionState.Cancelled, 3m);
        var query = new ObjectReadService(fixture.Context);

        var first = await query.SearchAsync("A_", 0, 1, CancellationToken.None);
        var next = await query.SearchAsync("A_", 1, 1, CancellationToken.None);
        var empty = await query.SearchAsync("no match", 0, 50, CancellationToken.None);

        Assert.Equal(a.Id, Assert.Single(first.Items).Id);
        Assert.True(first.HasMore);
        Assert.Equal(b.Id, Assert.Single(next.Items).Id);
        Assert.False(next.HasMore);
        Assert.Empty(empty.Items);
        Assert.False(empty.HasMore);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    /// <summary>
    /// Проверяет поиск по обозначению и стандартному наименованию без выбора отменённой текущей версии.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Search_finds_designation_and_standard_name_and_ignores_cancelled_current_version()
    {
        await using var fixture = await Fixture.OpenAsync();
        var designation = await fixture.CreateAsync(PdmObjectType.Part, "MATCH-DES", "Other", VersionState.InWork, null);
        var standard = await fixture.CreateStandardAsync("Fastener X", "Fastener X");
        var cancelled = await fixture.CreateAsync(PdmObjectType.Part, "NO-CURRENT", "Hidden word", VersionState.Cancelled, null);
        var query = new ObjectReadService(fixture.Context);

        Assert.Equal(designation.Id, Assert.Single((await query.SearchAsync("match-des", 0, 20, CancellationToken.None)).Items).Id);
        Assert.Equal(standard.Id, Assert.Single((await query.SearchAsync("fastener x", 0, 20, CancellationToken.None)).Items).Id);
        var noCurrent = Assert.Single((await query.SearchAsync("NO-CURRENT", 0, 20, CancellationToken.None)).Items);
        Assert.Equal(cancelled.Id, noCurrent.Id);
        Assert.True(noCurrent.NoCurrentVersion);
        Assert.Null(noCurrent.CurrentVersionId);
        var hiddenName = await query.SearchAsync("Hidden word", 0, 20, CancellationToken.None);
        Assert.Empty(hiddenName.Items);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    /// <summary>
    /// Проверяет выдачу текущей версии только при её допустимом состоянии и сохранение отменённых версий в истории.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Card_returns_current_only_when_valid_and_keeps_cancelled_versions_in_history()
    {
        await using var fixture = await Fixture.OpenAsync();
        var item = await fixture.CreateAsync(PdmObjectType.Part, "CARD-001", "First", VersionState.Approved, 4m);
        var newer = new ObjectVersion { ObjectId = item.Id, Version = 2, State = VersionState.Cancelled, Name = "Cancelled", Mass = 5m };
        fixture.Context.Versions.Add(newer);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var query = new ObjectReadService(fixture.Context);

        var current = await query.GetAsync(item.Id, null, CancellationToken.None);
        var history = await query.GetAsync(item.Id, 2, CancellationToken.None);
        var missing = await query.GetAsync(Guid.NewGuid(), null, CancellationToken.None);

        Assert.NotNull(current);
        Assert.Equal(item.CurrentVersionId, current!.CurrentVersionId);
        Assert.Equal(1, current.SelectedVersion!.Version);
        Assert.Equal(new[] { 2, 1 }, current.Versions.Select(v => v.Version));
        Assert.Equal(VersionState.Cancelled, history!.SelectedVersion!.State);
        Assert.Null(missing);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, PdmDbContext context)
        {
            _connection = connection;
            Context = context;
        }
        /// <summary>
        /// Контекст базы данных, используемый тестовой фикстурой.
        /// </summary>
        public PdmDbContext Context
        {
            get;
        }

        public static async Task<Fixture> OpenAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new PdmDbContext(new DbContextOptionsBuilder<PdmDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new Fixture(connection, context);
        }

        public async Task<PdmObject> CreateAsync(PdmObjectType type, string designation, string name, VersionState state, decimal? mass)
        {
            var item = new PdmObject { Type = type, Designation = designation };
            Context.Objects.Add(item);
            await Context.SaveChangesAsync();
            var version = new ObjectVersion { ObjectId = item.Id, Version = 1, State = state, Name = name, Mass = mass };
            Context.Versions.Add(version);
            await Context.SaveChangesAsync();
            item.CurrentVersionId = version.Id;
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
            return item;
        }

        public async Task<PdmObject> CreateStandardAsync(string standardName, string normalizedName)
        {
            var item = new PdmObject { Type = PdmObjectType.StandardPart, StandardName = standardName, NormalizedName = normalizedName };
            Context.Objects.Add(item);
            await Context.SaveChangesAsync();
            var version = new ObjectVersion { ObjectId = item.Id, Version = 1, State = VersionState.Approved, Mass = 0.1m };
            Context.Versions.Add(version);
            await Context.SaveChangesAsync();
            item.CurrentVersionId = version.Id;
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
            return item;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
