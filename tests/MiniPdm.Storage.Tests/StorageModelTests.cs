using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using MiniPdm.Domain.Composition;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Storage;
using Xunit;

namespace MiniPdm.Storage.Tests;

/// <summary>
/// Проверяет ограничения модели хранения, целостность связей и конкурентную запись.
/// </summary>
public sealed class StorageModelTests
{
    /// <summary>
    /// Проверяет настройку уникальных ключей, ограничений и связи текущей версии с тем же объектом.
    /// </summary>
    [Fact]
    public void Maps_unique_keys_checks_and_same_object_current_version_foreign_key()
    {
        using var db = new PdmDbContext(new DbContextOptionsBuilder<PdmDbContext>().UseNpgsql("Host=localhost;Database=test").Options);
        var model = db.GetService<IDesignTimeModel>().Model;
        var objects = model.FindEntityType(typeof(PdmObject))!;
        var versions = model.FindEntityType(typeof(ObjectVersion))!;
        var links = model.FindEntityType(typeof(BomLink))!;

        Assert.Contains(objects.GetIndexes(), i => i.IsUnique && i.GetFilter()!.Contains("\"Type\" IN (1, 2)"));
        Assert.Contains(objects.GetIndexes(), i => i.IsUnique && i.GetFilter()!.Contains("\"Type\" = 3"));
        Assert.Contains(versions.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual([nameof(ObjectVersion.ObjectId), nameof(ObjectVersion.Version)]));
        Assert.Contains(links.GetCheckConstraints(), c => c.Name == "ck_bom_links_quantity_positive");
        Assert.Contains(versions.GetCheckConstraints(), c => c.Name == "ck_object_versions_version_positive");

        var current = objects.GetForeignKeys().Single(fk => fk.Properties.Any(p => p.Name == nameof(PdmObject.CurrentVersionId)));
        Assert.Equal([nameof(PdmObject.CurrentVersionId), nameof(PdmObject.Id)], current.Properties.Select(p => p.Name));
        Assert.Equal([nameof(ObjectVersion.Id), nameof(ObjectVersion.ObjectId)], current.PrincipalKey.Properties.Select(p => p.Name));
        Assert.Equal(DeleteBehavior.Restrict, current.DeleteBehavior);
    }

    /// <summary>
    /// Проверяет ограничения SQLite на положительное количество и связь текущей версии с тем же объектом.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Sqlite_enforces_positive_quantity_and_same_object_current_version()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PdmDbContext>().UseSqlite(connection).Options;
        await using var db = new PdmDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var parent = await CreateObjectAndVersion(db, PdmObjectType.Assembly, "АБВГ.301245.001");
        var child = await CreateObjectAndVersion(db, PdmObjectType.Part, "ДЕЖЗ.301245.001");
        var parentVersion = await db.Versions.SingleAsync(v => v.ObjectId == parent.Id);
        var childVersion = await db.Versions.SingleAsync(v => v.ObjectId == child.Id);

        db.BomLinks.Add(new BomLink { ParentVersionId = parentVersion.Id, ChildObjectId = child.Id, Quantity = 0 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.BomLinks.Add(new BomLink { ParentVersionId = parentVersion.Id, ChildObjectId = child.Id, Quantity = 1 });
        await db.SaveChangesAsync();

        var invalid = new PdmObject { Type = PdmObjectType.Part, Designation = "ЖЗИЙ.301245.001", CurrentVersionId = childVersion.Id };
        db.Objects.Add(invalid);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <summary>
    /// Проверяет изменение токена родителя при добавлении связи, даже если родитель не загружен в контекст.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Adding_a_link_bumps_parent_token_even_when_parent_was_not_loaded()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PdmDbContext>().UseSqlite(connection).Options;
        await using (var db = new PdmDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            var parent = await CreateObjectAndVersion(db, PdmObjectType.Assembly, "АБВГ.301245.001");
            var child = await CreateObjectAndVersion(db, PdmObjectType.Part, "ДЕЖЗ.301245.001");
            var oldToken = parent.ConcurrencyToken;
            await using (var other = new PdmDbContext(options))
            {
                var parentVersion = await db.Versions.SingleAsync(v => v.ObjectId == parent.Id);
                other.BomLinks.Add(new BomLink { ParentVersionId = parentVersion.Id, ChildObjectId = child.Id, Quantity = 2 });
                await other.SaveChangesAsync();
            }
            await using var verification = new PdmDbContext(options);
            Assert.NotEqual(oldToken, (await verification.Objects.FindAsync(parent.Id))!.ConcurrencyToken);
        }
    }

    /// <summary>
    /// Проверяет, что две конкурентные записи с устаревшим токеном объекта не могут сохраниться одновременно.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Concurrent_writers_using_stale_object_token_cannot_both_save()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PdmDbContext>().UseSqlite(connection).Options;
        Guid objectId;
        await using (var seed = new PdmDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            var obj = await CreateObjectAndVersion(seed, PdmObjectType.Part, "АБВГ.301245.001");
            objectId = obj.Id;
            var version = await seed.Versions.SingleAsync(v => v.ObjectId == objectId);
            version.Name = "Исходное имя";
            await seed.SaveChangesAsync();
        }
        await using var first = new PdmDbContext(options);
        await using var second = new PdmDbContext(options);
        _ = await first.Objects.SingleAsync(x => x.Id == objectId);
        var aVersion = await first.Versions.SingleAsync(v => v.ObjectId == objectId);
        _ = await second.Objects.SingleAsync(x => x.Id == objectId);
        var bVersion = await second.Versions.SingleAsync(v => v.ObjectId == objectId);
        aVersion.Name = "Первое изменение";
        bVersion.Name = "Устаревшее изменение";
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var verification = new PdmDbContext(options);
        Assert.Equal("Первое изменение", await verification.Versions.Select(v => v.Name).SingleAsync());
    }

    /// <summary>
    /// Проверяет отклонение массы, которая потеряла бы точность при округлении до numeric(18,6).
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Rejects_mass_that_would_be_rounded_by_numeric_18_6()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PdmDbContext>().UseSqlite(connection).Options;
        await using var db = new PdmDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var obj = new PdmObject { Type = PdmObjectType.Part, Designation = "АБВГ.301245.001" };
        db.Objects.Add(obj);
        await db.SaveChangesAsync();
        db.Versions.Add(new ObjectVersion { ObjectId = obj.Id, Version = 1, Mass = 1.1234567m });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private static async Task<PdmObject> CreateObjectAndVersion(PdmDbContext db, PdmObjectType type, string designation)
    {
        var obj = new PdmObject { Type = type, Designation = designation };
        db.Objects.Add(obj);
        await db.SaveChangesAsync();
        var version = new ObjectVersion { ObjectId = obj.Id, Version = 1 };
        db.Versions.Add(version);
        await db.SaveChangesAsync();
        obj.CurrentVersionId = version.Id;
        await db.SaveChangesAsync();
        return obj;
    }
}
