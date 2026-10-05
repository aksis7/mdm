using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniPdm.Domain.Composition;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Domain.Versions.Mutations;
using MiniPdm.Modules.Versions.Services;
using MiniPdm.Storage;
using Xunit;

namespace MiniPdm.Modules.Tests;

/// <summary>
/// Проверяет сохранение изменений состава через сервис управления версиями.
/// </summary>
public sealed class VersionMutationServiceTests
{
    /// <summary>
    /// Проверяет объединение повторных дочерних элементов и сохранение состава через сервис модуля.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task ReplaceComposition_merges_duplicate_children_and_persists_through_module_service()
    {
        await using var fixture = await Fixture.OpenAsync();
        var parent = new PdmObject { Type = PdmObjectType.Assembly, Designation = "АБВГ.301245.001" };
        var child = new PdmObject { Type = PdmObjectType.Part, Designation = "ДЕЖЗ.301245.001" };
        fixture.Context.Objects.AddRange(parent, child);
        await fixture.Context.SaveChangesAsync();
        var parentVersion = new ObjectVersion { ObjectId = parent.Id, Version = 1, State = VersionState.InWork };
        var childVersion = new ObjectVersion { ObjectId = child.Id, Version = 1, State = VersionState.InWork, Mass = 1m };
        fixture.Context.Versions.AddRange(parentVersion, childVersion);
        await fixture.Context.SaveChangesAsync();
        parent.CurrentVersionId = parentVersion.Id;
        child.CurrentVersionId = childVersion.Id;
        await fixture.Context.SaveChangesAsync();
        var expectedToken = parent.ConcurrencyToken;
        fixture.Context.ChangeTracker.Clear();

        var result = await new VersionMutationService(fixture.Context).ReplaceCompositionAsync(parent.Id, 1,
            [new CompositionItem(child.Id, 2), new CompositionItem(child.Id, 3)], expectedToken, CancellationToken.None);

        Assert.Equal(VersionMutationStatus.Succeeded, result.Status);
        Assert.Single(result.Warnings);
        await using var verify = new PdmDbContext(fixture.Options);
        var savedLink = await verify.BomLinks.SingleAsync(x => x.ParentVersionId == parentVersion.Id);
        Assert.Equal(child.Id, savedLink.ChildObjectId);
        Assert.Equal(5, savedLink.Quantity);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, DbContextOptions<PdmDbContext> options)
        {
            _connection = connection;
            Options = options;
            Context = new PdmDbContext(options);
        }

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

        public static async Task<Fixture> OpenAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<PdmDbContext>().UseSqlite(connection).Options;
            await using (var context = new PdmDbContext(options))
                await context.Database.EnsureCreatedAsync();
            return new Fixture(connection, options);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
