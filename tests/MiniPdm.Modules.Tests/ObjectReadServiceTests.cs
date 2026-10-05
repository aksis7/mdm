using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniPdm.Contracts.Modules.Objects.DtoModels;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Modules.Objects.Services;
using MiniPdm.Storage;
using Xunit;

namespace MiniPdm.Modules.Tests;

/// <summary>
/// Проверяет чтение карточек и поиск объектов с текущей версией и историей.
/// </summary>
public sealed class ObjectReadServiceTests
{
    /// <summary>
    /// Проверяет перенос стандартного наименования, текущей версии и истории из базы данных в карточку объекта.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task GetObject_maps_standard_name_current_version_and_history_from_database()
    {
        await using var fixture = await Fixture.OpenAsync();
        var item = await fixture.CreateStandardAsync("M8 screw", VersionState.Approved, current: true);
        await fixture.AddVersionAsync(item, 2, VersionState.Cancelled, "Old label", makeCurrent: false);
        fixture.Context.ChangeTracker.Clear();
        var service = new ObjectReadService(fixture.Context);

        var current = await service.GetObjectAsync(item.Id, null, CancellationToken.None);
        var historical = await service.GetObjectAsync(item.Id, 2, CancellationToken.None);

        Assert.Equal("M8 screw", current!.Name);
        Assert.True(current.SelectedVersion!.IsCurrent);
        Assert.Equal("M8 screw", current.SelectedVersion.Name);
        Assert.Equal(new[] { 2, 1 }, current.Versions.Select(x => x.Version));
        Assert.Equal("Cancelled", historical!.SelectedVersion!.State);
        Assert.False(historical.SelectedVersion.IsCurrent);
        Assert.Equal(item.CurrentVersionId, historical.CurrentVersionId);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    /// <summary>
    /// Проверяет возврат объекта без текущей версии и пустой результат для неизвестного объекта или версии.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task GetObject_keeps_object_without_current_version_and_returns_null_for_missing_object_or_version()
    {
        await using var fixture = await Fixture.OpenAsync();
        var item = await fixture.CreateAsync(PdmObjectType.Part, "АБВГ.301245.001", "No current", VersionState.Cancelled, current: false);
        var service = new ObjectReadService(fixture.Context);

        var noCurrent = await service.GetObjectAsync(item.Id, null, CancellationToken.None);

        Assert.Null(noCurrent!.CurrentVersionId);
        Assert.Null(noCurrent.SelectedVersion);
        Assert.Equal("NoCurrentVersion", noCurrent.ErrorCode);
        Assert.Null(await service.GetObjectAsync(Guid.NewGuid(), null, CancellationToken.None));
        Assert.Null(await service.GetObjectAsync(item.Id, 7, CancellationToken.None));
    }

    /// <summary>
    /// Проверяет выдачу публичных DTO со стабильной пагинацией и данными текущей версии.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Search_returns_public_dtos_with_stable_paging_and_current_version_fields()
    {
        await using var fixture = await Fixture.OpenAsync();
        var first = await fixture.CreateAsync(PdmObjectType.Part, "A_001", "Alpha", VersionState.Approved, current: true);
        var second = await fixture.CreateAsync(PdmObjectType.Part, "A_002", "Beta", VersionState.InWork, current: true);
        await fixture.CreateAsync(PdmObjectType.Part, "A_003", "Cancelled name", VersionState.Cancelled, current: false);
        var service = new ObjectReadService(fixture.Context);

        ObjectSearchPageDto page = await service.SearchObjectsAsync("A_", 0, 1, CancellationToken.None);
        ObjectSearchPageDto next = await service.SearchObjectsAsync("A_", 1, 1, CancellationToken.None);
        var cancelled = await service.SearchObjectsAsync("A_003", 0, 10, CancellationToken.None);

        Assert.Equal(first.Id, Assert.Single(page.Items).Id);
        Assert.True(page.HasMore);
        Assert.Equal(second.Id, Assert.Single(next.Items).Id);
        Assert.True(next.HasMore);
        var noCurrent = Assert.Single(cancelled.Items);
        Assert.True(noCurrent.NoCurrentVersion);
        Assert.Null(noCurrent.CurrentVersionId);
        Assert.Null(noCurrent.State);
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

        public async Task<PdmObject> CreateAsync(PdmObjectType type, string designation, string name, VersionState state, bool current)
        {
            var item = new PdmObject { Type = type, Designation = designation };
            Context.Objects.Add(item);
            await Context.SaveChangesAsync();
            await AddVersionAsync(item, 1, state, name, current);
            Context.ChangeTracker.Clear();
            return await Context.Objects.AsNoTracking().SingleAsync(x => x.Id == item.Id);
        }

        public async Task<PdmObject> CreateStandardAsync(string name, VersionState state, bool current)
        {
            var item = new PdmObject { Type = PdmObjectType.StandardPart, StandardName = name, NormalizedName = name.ToUpperInvariant() };
            Context.Objects.Add(item);
            await Context.SaveChangesAsync();
            await AddVersionAsync(item, 1, state, name, current);
            Context.ChangeTracker.Clear();
            return await Context.Objects.AsNoTracking().SingleAsync(x => x.Id == item.Id);
        }

        /// <summary>
        /// Создаёт и сохраняет тестовую версию объекта с заданными параметрами.
        /// </summary>
        /// <param name="item">Объект, для которого создаётся версия.</param>
        /// <param name="number">Номер создаваемой версии.</param>
        /// <param name="state">Состояние версии.</param>
        /// <param name="name">Наименование версии.</param>
        /// <param name="makeCurrent">Указывает, следует ли назначить версию текущей.</param>
        /// <returns>Задача завершается после выполнения проверок теста.</returns>
        public async Task AddVersionAsync(PdmObject item, int number, VersionState state, string name, bool makeCurrent)
        {
            var version = new ObjectVersion { ObjectId = item.Id, Version = number, State = state, Name = name, Mass = 1m };
            Context.Versions.Add(version);
            await Context.SaveChangesAsync();
            if (makeCurrent)
            {
                item.CurrentVersionId = version.Id;
                await Context.SaveChangesAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
