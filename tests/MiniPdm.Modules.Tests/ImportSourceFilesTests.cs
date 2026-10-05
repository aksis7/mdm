using Microsoft.Extensions.Options;
using MiniPdm.Modules.Import.Abstractions;
using MiniPdm.Modules.Import.Infrastructure.SourceFiles;
using MiniPdm.Modules.Import.Abstractions.Database;
using MiniPdm.Modules.Import.DtoModels.Database;
using Xunit;

namespace MiniPdm.Modules.Tests;

/// <summary>
/// Проверяет загрузку, продвижение, восстановление и очистку файловых источников импорта.
/// </summary>
public sealed class ImportSourceFilesTests
{
    /// <summary>
    /// Проверяет независимость каталогов и очистки отдельных попыток загрузки.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task UploadAttemptsAreIsolatedAndDisposedIndependently()
    {
        using var fixture = new Fixture();
        var storage = fixture.CreateStorage();
        var id = Guid.NewGuid();
        await using var first = await storage.StageAsync(id, [Upload("part.m3d", "one")], CancellationToken.None);
        await using var second = await storage.StageAsync(id, [Upload("part.m3d", "two")], CancellationToken.None);

        Assert.NotEqual(first.SourceDescriptor.Location, second.SourceDescriptor.Location);
        await first.DisposeAsync();
        Assert.False(Directory.Exists(first.SourceDescriptor.Location));
        Assert.Equal("two", await File.ReadAllTextAsync(Path.Combine(second.SourceDescriptor.Location, "part.m3d")));
    }

    /// <summary>
    /// Проверяет отклонение имён файлов, небезопасных для загрузки.
    /// </summary>
    /// <param name="fileName">Имя тестового файла.</param>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Theory]
    [InlineData("../escape.m3d")]
    [InlineData("folder/part.m3d")]
    [InlineData("part.txt")]
    [InlineData("bad\nname.m3d")]
    public async Task UploadRejectsUnsafeNames(string fileName)
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<ImportUploadValidationException>(() => fixture.CreateStorage().StageAsync(
            Guid.NewGuid(), [Upload(fileName, "x")], CancellationToken.None));
    }

    /// <summary>
    /// Проверяет ограничение размера при потоковой загрузке и очистку незавершённой попытки.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task UploadEnforcesStreamingSizeLimitAndCleansPartialAttempt()
    {
        using var fixture = new Fixture();
        var storage = fixture.CreateStorage(new ImportStorageOptions { DataRoot = fixture.Root, MaxFileBytes = 3, MaxTotalBytes = 8 });
        await Assert.ThrowsAsync<ImportUploadValidationException>(() => storage.StageAsync(
            Guid.NewGuid(), [Upload("large.m3d", "four")], CancellationToken.None));
        Assert.Empty(Directory.Exists(Path.Combine(fixture.Root, "uploads"))
            ? Directory.EnumerateDirectories(Path.Combine(fixture.Root, "uploads"), "*", SearchOption.AllDirectories)
                .Where(path => Guid.TryParseExact(Path.GetFileName(path), "N", out _))
            : Enumerable.Empty<string>());
    }

    /// <summary>
    /// Проверяет удаление каталога попытки при отмене загрузки.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task CancelledUploadCleansItsAttemptDirectory()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.CreateStorage().StageAsync(
            Guid.NewGuid(), [Upload("part.m3d", "body")], cancellation.Token));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "uploads")));
    }

    /// <summary>
    /// Проверяет копирование только принятых файлов и ограничение компенсации одним импортом.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task PromotionCopiesOnlyAcceptedFilesAndCompensationIsImportScoped()
    {
        using var fixture = new Fixture();
        var storage = fixture.CreateStorage();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        await using var source = await storage.StageAsync(firstId,
            [Upload("accepted.m3d", "accepted"), Upload("rejected.a3d", "rejected")], CancellationToken.None);
        await storage.PromoteAsync(firstId, source.SourceDescriptor, ["accepted.m3d"], CancellationToken.None);
        await storage.PromoteAsync(secondId, source.SourceDescriptor, ["accepted.m3d"], CancellationToken.None);

        Assert.Equal("accepted", await File.ReadAllTextAsync(storage.GetSourceReference(firstId, "accepted.m3d")));
        Assert.False(File.Exists(storage.GetSourceReference(firstId, "rejected.a3d")));
        await storage.CompensateAsync(firstId, CancellationToken.None);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "imports", firstId.ToString("D"))));
        Assert.True(File.Exists(storage.GetSourceReference(secondId, "accepted.m3d")));
    }

    /// <summary>
    /// Проверяет, что продвижение файлов не перезаписывает существующую папку импорта.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task PromotionNeverOverwritesExistingImportFolder()
    {
        using var fixture = new Fixture();
        var storage = fixture.CreateStorage();
        var id = Guid.NewGuid();
        await using var source = await storage.StageAsync(id, [Upload("part.m3d", "source")], CancellationToken.None);
        await storage.PromoteAsync(id, source.SourceDescriptor, ["part.m3d"], CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => storage.PromoteAsync(id, source.SourceDescriptor, ["part.m3d"], CancellationToken.None));
        Assert.Equal("source", await File.ReadAllTextAsync(storage.GetSourceReference(id, "part.m3d")));
    }

    /// <summary>
    /// Проверяет удаление только потерянных продвижений и папок подтверждённого отката при восстановлении.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task RecoveryDeletesOrphanPromotionsAndConfirmedRollbackFoldersOnly()
    {
        using var fixture = new Fixture();
        var storage = fixture.CreateStorage();
        var orphanId = Guid.NewGuid();
        var orphan = Path.Combine(fixture.Root, "imports", orphanId.ToString("D"));
        Directory.CreateDirectory(orphan);
        var completedId = Guid.NewGuid();
        var completed = Path.Combine(fixture.Root, "imports", completedId.ToString("D"));
        Directory.CreateDirectory(completed);
        var promotingId = Guid.NewGuid();
        var promoting = Path.Combine(fixture.Root, "imports", $".{promotingId:D}.{Guid.NewGuid():N}.promoting");
        Directory.CreateDirectory(promoting);

        var db = new FakeImportPersistence(id => id == completedId
            ? new ImportPersistenceResultDto
            {
                State = ImportCommitState.Completed,
                Replayed = true,
                ReportJson = "{}"
            } : new ImportPersistenceResultDto
            {
                State = ImportCommitState.ConfirmedRollback,
                Replayed = false,
                ReportJson = null
            });
        var recovery = CreateRecovery(fixture, storage, db);

        var deleted = await recovery.RecoverAsync();

        Assert.Equal(2, deleted);
        Assert.False(Directory.Exists(orphan));
        Assert.False(Directory.Exists(promoting));
        Assert.True(Directory.Exists(completed));
    }

    /// <summary>
    /// Проверяет сообщение о сбое базы данных и сохранение папок при подробном восстановлении.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task DetailedRecoveryReportsDatabaseFailureAndRetainsFolders()
    {
        using var fixture = new Fixture();
        var storage = fixture.CreateStorage();
        var id = Guid.NewGuid();
        var orphan = Path.Combine(fixture.Root, "imports", id.ToString("D"));
        Directory.CreateDirectory(orphan);
        var recovery = CreateRecovery(fixture, storage, new FakeImportPersistence(_ => throw new InvalidOperationException("database unavailable")));

        var result = await recovery.RecoverDetailedAsync();

        Assert.Equal(0, result.RemovedCount);
        var error = Assert.Single(result.Errors);
        Assert.Contains($"imports/{id:D}", error);
        Assert.DoesNotContain(fixture.Root, error, StringComparison.Ordinal);
        Assert.True(Directory.Exists(orphan));
    }

    /// <summary>
    /// Проверяет сохранение папки продвижения при неизвестном результате операции базы данных.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task PromotionFolderWithUnknownDatabaseOutcomeIsRetained()
    {
        using var fixture = new Fixture();
        var storage = fixture.CreateStorage();
        var id = Guid.NewGuid();
        var folder = Path.Combine(fixture.Root, "imports", $".{id:D}.{Guid.NewGuid():N}.promoting");
        Directory.CreateDirectory(folder);
        var recovery = CreateRecovery(fixture, storage, new FakeImportPersistence(_ =>
            new ImportPersistenceResultDto
            {
                State = ImportCommitState.Unknown,
                Replayed = false,
                ReportJson = null,
                Error = "connection unavailable"
            }));

        var result = await recovery.RecoverDetailedAsync();

        Assert.Equal(0, result.RemovedCount);
        Assert.Contains(result.Errors, error => error.Contains(Path.GetFileName(folder), StringComparison.Ordinal));
        Assert.True(Directory.Exists(folder));
    }

    /// <summary>
    /// Проверяет пропуск активной аренды загрузки и удаление оставленной попытки.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task RecoverySkipsActiveUploadLeaseAndDeletesAbandonedAttempt()
    {
        using var fixture = new Fixture();
        var storage = fixture.CreateStorage();
        var id = Guid.NewGuid();
        await using var active = await storage.StageAsync(id, [Upload("active.m3d", "active")], CancellationToken.None);
        var stale = Path.Combine(fixture.Root, "uploads", id.ToString("D"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stale);
        await File.WriteAllTextAsync(Path.Combine(stale, ".active"), string.Empty);
        var recovery = CreateRecovery(fixture, storage, new FakeImportPersistence(_ => new ImportPersistenceResultDto
        {
            State = ImportCommitState.Completed,
            Replayed = true,
            ReportJson = "{}"
        }));

        Assert.Equal(1, await recovery.RecoverAsync());
        Assert.True(Directory.Exists(active.SourceDescriptor.Location));
        Assert.False(Directory.Exists(stale));
    }

    private static ImportUploadFile Upload(string name, string body) => new(name, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body)));

    private sealed class Fixture : IDisposable
    {
        /// <summary>
        /// Корневой временный каталог файлового хранилища теста.
        /// </summary>
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"import-storage-{Guid.NewGuid():N}");
        public FileImportStorage CreateStorage(ImportStorageOptions? options = null) => new(Options.Create(options ?? new ImportStorageOptions { DataRoot = Root }));

        /// <summary>
        /// Удаляет временный каталог файлов после завершения теста.
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private static ImportSourceRecovery CreateRecovery(Fixture fixture, FileImportStorage storage, IImportDatabaseService persistence) =>
        new(Options.Create(new ImportStorageOptions { DataRoot = fixture.Root }), storage, storage, persistence);

    private sealed class FakeImportPersistence(Func<Guid, ImportPersistenceResultDto> resolve) : IImportDatabaseService
    {
        public Task<ImportPersistenceResultDto> ExecuteAsync(Guid importId, ImportLookupDto lookup, Func<ImportSnapshotDto, CancellationToken, Task<ImportWritePlanDto>> prepare, CancellationToken ct) => throw new NotSupportedException();
        public Task<ImportPersistenceResultDto?> FindAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<ImportPersistenceResultDto> ResolveAsync(Guid id, CancellationToken ct) => Task.FromResult(resolve(id));
        public async Task<bool> CompensateIfRolledBackAsync(Guid id, Func<CancellationToken, Task> compensate, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (resolve(id).State == ImportCommitState.Completed)
                return false;
            await compensate(ct);
            return true;
        }
    }
}
