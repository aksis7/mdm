using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MiniPdm.Domain.Composition;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Storage;
using MiniPdm.Modules.Composition.Services;
using Xunit;

namespace MiniPdm.Storage.Tests;

/// <summary>
/// Проверяет чтение исторического и отменённого состава версии одним запросом без отслеживания.
/// </summary>
public sealed class VersionCompositionReadQueryTests
{
    /// <summary>
    /// Проверяет чтение выбранного исторического или отменённого состава одним запросом без отслеживания и сохранение пустых версий.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task ReadsSelectedHistoricalOrCancelledBomInOneNoTrackingQueryAndRetainsEmptyVersions()
    {
        var interceptor = new SelectCountingInterceptor();
        await using var fixture = await Fixture.OpenAsync(interceptor);
        var parent = new PdmObject { Type = PdmObjectType.Assembly, Designation = "PARENT" };
        var part = new PdmObject { Type = PdmObjectType.Part, Designation = "PART" };
        var standard = new PdmObject { Type = PdmObjectType.StandardPart, StandardName = "Bearing Original", NormalizedName = "BEARING ORIGINAL" };
        fixture.Context.Objects.AddRange(parent, part, standard);
        await fixture.Context.SaveChangesAsync();

        var historical = new ObjectVersion { ObjectId = parent.Id, Version = 1, State = VersionState.Cancelled };
        historical.Components.Add(new BomLink { ParentVersionId = historical.Id, ChildObjectId = part.Id, Quantity = 4 });
        historical.Components.Add(new BomLink { ParentVersionId = historical.Id, ChildObjectId = standard.Id, Quantity = 2 });
        var current = new ObjectVersion { ObjectId = parent.Id, Version = 2, State = VersionState.Approved };
        var empty = new ObjectVersion { ObjectId = parent.Id, Version = 3, State = VersionState.InWork };
        var cancelledChild = new ObjectVersion { ObjectId = part.Id, Version = 1, State = VersionState.Cancelled, Name = "Old part" };
        var standardVersion = new ObjectVersion { ObjectId = standard.Id, Version = 1, State = VersionState.Approved };
        fixture.Context.Versions.AddRange(historical, current, empty, cancelledChild, standardVersion);
        await fixture.Context.SaveChangesAsync();
        parent.CurrentVersionId = current.Id;
        part.CurrentVersionId = cancelledChild.Id;
        standard.CurrentVersionId = standardVersion.Id;
        await fixture.Context.SaveChangesAsync();
        var token = parent.ConcurrencyToken;
        fixture.Context.ChangeTracker.Clear();
        interceptor.Reset();
        var query = new VersionCompositionReadService(fixture.Context);

        var historicalResult = await query.ReadAsync(parent.Id, 1, CancellationToken.None);
        Assert.Equal(1, interceptor.SelectCount);
        Assert.NotNull(historicalResult);
        Assert.Equal(token, historicalResult!.ConcurrencyToken);
        Assert.Equal(new[] { part.Id, standard.Id }.OrderBy(x => x),
            historicalResult.Items.Select(x => x.ChildObjectId).OrderBy(x => x));
        var partItem = historicalResult.Items.Single(x => x.ChildObjectId == part.Id);
        var standardItem = historicalResult.Items.Single(x => x.ChildObjectId == standard.Id);
        Assert.True(partItem.NoCurrentVersion);
        Assert.Null(partItem.Name);
        Assert.Equal("Bearing Original", standardItem.Name);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());

        interceptor.Reset();
        var historicalDto = await query.GetVersionCompositionAsync(parent.Id, 1, CancellationToken.None);
        Assert.Equal(1, interceptor.SelectCount);
        Assert.NotNull(historicalDto);
        Assert.Equal(token, historicalDto!.ConcurrencyToken);
        Assert.Equal(2, historicalDto.Items.Count);
        Assert.True(historicalDto.Items.Single(x => x.ChildObjectId == part.Id).NoCurrentVersion);
        Assert.Equal("StandardPart", historicalDto.Items.Single(x => x.ChildObjectId == standard.Id).Type);

        interceptor.Reset();
        var emptyResult = await query.ReadAsync(parent.Id, 3, CancellationToken.None);
        Assert.Equal(1, interceptor.SelectCount);
        Assert.Empty(emptyResult!.Items);

        interceptor.Reset();
        Assert.Null(await query.ReadAsync(parent.Id, 99, CancellationToken.None));
        Assert.Equal(1, interceptor.SelectCount);
        interceptor.Reset();
        Assert.Null(await query.ReadAsync(Guid.NewGuid(), 1, CancellationToken.None));
        Assert.Equal(1, interceptor.SelectCount);
        Assert.Equal(current.Id, parent.CurrentVersionId);
        Assert.Equal(token, parent.ConcurrencyToken);
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

        public static async Task<Fixture> OpenAsync(DbCommandInterceptor interceptor)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new PdmDbContext(new DbContextOptionsBuilder<PdmDbContext>()
                .UseSqlite(connection).AddInterceptors(interceptor).Options);
            await context.Database.EnsureCreatedAsync();
            return new Fixture(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class SelectCountingInterceptor : DbCommandInterceptor
    {
        /// <summary>
        /// Число команд SELECT, перехваченных тестовым перехватчиком.
        /// </summary>
        public int SelectCount
        {
            get; private set;
        }

        /// <summary>
        /// Сбрасывает счётчик команд SELECT, зарегистрированных перехватчиком.
        /// </summary>
        public void Reset() => SelectCount = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                SelectCount++;
            return ValueTask.FromResult(result);
        }
    }
}
