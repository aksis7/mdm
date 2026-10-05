using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniPdm.Storage;
using MiniPdm.Modules.BackgroundTasks.Abstractions.Database;
using MiniPdm.Modules.BackgroundTasks.DtoModels;
using MiniPdm.Modules.BackgroundTasks.Services.Database;
using Xunit;

namespace MiniPdm.Storage.Tests;

/// <summary>
/// Проверяет сохранение расписания, запусков и восстановления прерванных фоновых задач.
/// </summary>
public sealed class BackgroundTaskPersistenceTests
{
    /// <summary>
    /// Проверяет сохранение расписания и статуса выполнения, а также расчёт следующего запуска по обновлённому интервалу.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Schedule_and_run_status_persist_and_scheduled_time_uses_updated_interval()
    {
        await using var fixture = await Fixture.CreateAsync();
        var definition = new BackgroundTaskDefinitionRecordDto
        {
            Id = "test-job",
            Name = "Test job",
            DefaultIntervalMinutes = 1440
        };
        var createdAt = DateTimeOffset.Parse("2026-10-01T10:00:00Z");
        await new BackgroundTaskDatabaseService(fixture.Context).EnsureDefinitionsAsync([definition], createdAt, CancellationToken.None);

        var initial = await new BackgroundTaskDatabaseService(fixture.Context).GetAsync(definition.Id, CancellationToken.None);
        Assert.Equal("Idle", initial!.State);
        Assert.Equal(1440, initial.IntervalMinutes);
        Assert.Equal(createdAt, initial.NextRunAt);

        var scheduleChangedAt = createdAt.AddMinutes(2);
        Assert.True(await new BackgroundTaskDatabaseService(fixture.Context).UpdateScheduleAsync(definition.Id, 15, scheduleChangedAt, CancellationToken.None));
        var scheduled = await new BackgroundTaskDatabaseService(fixture.Context).GetAsync(definition.Id, CancellationToken.None);
        var nextRunDuringRun = scheduled!.NextRunAt;
        Assert.Equal(scheduleChangedAt.AddMinutes(15), nextRunDuringRun);

        var startedAt = scheduleChangedAt.AddMinutes(1);
        Assert.True(await new BackgroundTaskDatabaseService(fixture.Context).TryStartAsync(definition.Id, startedAt, CancellationToken.None));
        Assert.False(await new BackgroundTaskDatabaseService(fixture.Context).TryStartAsync(definition.Id, startedAt.AddSeconds(1), CancellationToken.None));
        Assert.True(await new BackgroundTaskDatabaseService(fixture.Context).UpdateScheduleAsync(definition.Id, 30, startedAt, CancellationToken.None));
        var running = await new BackgroundTaskDatabaseService(fixture.Context).GetAsync(definition.Id, CancellationToken.None);
        Assert.Equal("Running", running!.State);
        Assert.Equal(startedAt, running.LastStartedAt);
        Assert.Equal(nextRunDuringRun, running.NextRunAt);

        var completedAt = startedAt.AddSeconds(12);
        Assert.True(await new BackgroundTaskDatabaseService(fixture.Context).CompleteAsync(
            definition.Id, "PartiallySucceeded", completedAt, "Removed 2 entries.", "One source folder was inaccessible.", CancellationToken.None));

        await using var verify = new PdmDbContext(fixture.Options);
        var persisted = await new BackgroundTaskDatabaseService(verify).GetAsync(definition.Id, CancellationToken.None);
        Assert.Equal("PartiallySucceeded", persisted!.State);
        Assert.Equal(30, persisted.IntervalMinutes);
        Assert.Equal(startedAt, persisted.LastStartedAt);
        Assert.Equal(completedAt, persisted.LastCompletedAt);
        Assert.Equal("Removed 2 entries.", persisted.LastResult);
        Assert.Equal("One source folder was inaccessible.", persisted.LastError);
        Assert.Equal(completedAt.AddMinutes(30), persisted.NextRunAt);
    }

    /// <summary>
    /// Проверяет перевод выполнявшейся задачи в состояние прерванной при повторной регистрации после перезапуска.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Existing_running_job_is_marked_interrupted_when_definitions_are_ensured_after_restart()
    {
        await using var fixture = await Fixture.CreateAsync();
        var definition = new BackgroundTaskDefinitionRecordDto
        {
            Id = "test-job",
            Name = "Test job",
            DefaultIntervalMinutes = 1440
        };
        var now = DateTimeOffset.Parse("2026-10-01T10:00:00Z");
        var repository = new BackgroundTaskDatabaseService(fixture.Context);
        await repository.EnsureDefinitionsAsync([definition], now, CancellationToken.None);
        Assert.True(await repository.TryStartAsync(definition.Id, now, CancellationToken.None));

        await repository.EnsureDefinitionsAsync([definition], now.AddMinutes(3), CancellationToken.None);

        var interrupted = await repository.GetAsync(definition.Id, CancellationToken.None);
        Assert.Equal("Interrupted", interrupted!.State);
        Assert.Equal("The previous process stopped while this task was running.", interrupted.LastError);
        Assert.Equal(now, interrupted.LastStartedAt);
    }

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

        private Fixture(SqliteConnection connection, DbContextOptions<PdmDbContext> options)
        {
            _connection = connection;
            Options = options;
            Context = new PdmDbContext(options);
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
}
