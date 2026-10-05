using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MiniPdm.Contracts.Modules.BackgroundTasks.DtoModels;
using MiniPdm.Modules.BackgroundTasks.Abstractions;
using MiniPdm.Modules.BackgroundTasks.Services;
using MiniPdm.Modules.BackgroundTasks.Abstractions.Database;
using MiniPdm.Modules.BackgroundTasks.DtoModels;
using Xunit;

namespace MiniPdm.Modules.Tests.BackgroundTasks.Services;

/// <summary>
/// Проверяет последовательность ручных и плановых запусков, обработку ошибок хранилища и остановку координатора.
/// </summary>
public sealed class BackgroundTaskCoordinatorTests
{
    /// <summary>
    /// Проверяет сохранение ручного запуска до подтверждения и его взаимное исключение с изменением расписания.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Manual_run_is_persisted_before_acceptance_and_cannot_overlap_schedule_updates()
    {
        var persistence = new FakePersistence();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;
        var definition = new BackgroundTaskDefinition("test-job", "Test job", 1440, async (_, ct) =>
        {
            Interlocked.Increment(ref executions);
            entered.TrySetResult(true);
            await release.Task.WaitAsync(ct);
            return new("Removed 3 entries.", []);
        });
        using var services = CreateServices(persistence);
        var coordinator = CreateCoordinator(definition, services);

        var accepted = await coordinator.RequestManualRunAsync(definition.Id, CancellationToken.None);
        Assert.Equal(BackgroundTaskRunRequestStatus.Accepted, accepted.Status);
        var running = await persistence.GetAsync(definition.Id, CancellationToken.None);
        Assert.Equal("Running", running!.State);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var duplicate = await coordinator.RequestManualRunAsync(definition.Id, CancellationToken.None);
        Assert.Equal(BackgroundTaskRunRequestStatus.Running, duplicate.Status);
        var updated = await coordinator.UpdateScheduleAsync(definition.Id, 20, CancellationToken.None);
        Assert.Equal(20, updated!.IntervalMinutes);
        Assert.Equal("Running", updated.State);
        Assert.Equal(running.LastStartedAt, updated.LastStartedAt);
        Assert.Equal(running.NextRunAt, updated.NextRunAt);

        release.TrySetResult(true);
        var finished = await WaitForStateAsync(persistence, "Succeeded");
        Assert.Equal(1, Volatile.Read(ref executions));
        Assert.Equal("Removed 3 entries.", finished.LastResult);
        Assert.Equal(finished.LastCompletedAt!.Value.AddMinutes(20), finished.NextRunAt);
        await coordinator.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Проверяет, что ошибка сохранения начала не запускает задачу и не возвращает подтверждение.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Failed_start_persistence_does_not_run_the_job_or_return_accepted()
    {
        var persistence = new FakePersistence { FailStart = true };
        var executed = false;
        var definition = new BackgroundTaskDefinition("test-job", "Test job", 1440, (_, _) =>
        {
            executed = true;
            return Task.FromResult(new BackgroundTaskExecutionResult("done", []));
        });
        using var services = CreateServices(persistence);
        var coordinator = CreateCoordinator(definition, services);

        await Assert.ThrowsAsync<BackgroundTaskPersistenceUnavailableException>(() =>
            coordinator.RequestManualRunAsync(definition.Id, CancellationToken.None));

        Assert.False(executed);
        await coordinator.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Проверяет повторную запись результата без повторного выполнения и фиксацию отмены при остановке.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Completion_status_write_retries_without_rerunning_job_and_shutdown_marks_cancellation_interrupted()
    {
        var persistence = new FakePersistence { FailCompletions = 1 };
        var options = new BackgroundTaskCoordinatorOptions(TimeSpan.FromMilliseconds(10));
        var executions = 0;
        var definition = new BackgroundTaskDefinition("test-job", "Test job", 1440, (_, _) =>
        {
            Interlocked.Increment(ref executions);
            return Task.FromResult(new BackgroundTaskExecutionResult("done", ["One item failed."]));
        });
        using var services = CreateServices(persistence);
        var coordinator = CreateCoordinator(definition, services, options);

        Assert.Equal(BackgroundTaskRunRequestStatus.Accepted,
            (await coordinator.RequestManualRunAsync(definition.Id, CancellationToken.None)).Status);
        var partial = await WaitForStateAsync(persistence, "PartiallySucceeded");
        Assert.Equal(1, Volatile.Read(ref executions));
        Assert.Equal(2, persistence.CompletionAttempts);
        Assert.Equal("One item failed.", partial.LastError);
        await coordinator.StopAsync(CancellationToken.None);

        var cancelPersistence = new FakePersistence();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocking = new BackgroundTaskDefinition("cancel-job", "Cancel job", 1440, async (_, ct) =>
        {
            entered.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new("unreachable", []);
        });
        using var cancelServices = CreateServices(cancelPersistence);
        var cancelCoordinator = CreateCoordinator(blocking, cancelServices);
        Assert.Equal(BackgroundTaskRunRequestStatus.Accepted,
            (await cancelCoordinator.RequestManualRunAsync(blocking.Id, CancellationToken.None)).Status);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await cancelCoordinator.StopAsync(CancellationToken.None);

        Assert.Equal("Interrupted", (await cancelPersistence.GetAsync(blocking.Id, CancellationToken.None))!.State);
    }

    /// <summary>
    /// Проверяет сохранение состояния «выполняется» до восстановления хранилища или перезапуска при сбое записи результата.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Completion_storage_failure_keeps_status_running_until_recovery_or_restart()
    {
        var persistence = new FakePersistence { FailEveryCompletion = true };
        var executions = 0;
        var definition = new BackgroundTaskDefinition("test-job", "Test job", 1440, (_, _) =>
        {
            Interlocked.Increment(ref executions);
            return Task.FromResult(new BackgroundTaskExecutionResult("done", []));
        });
        using var services = CreateServices(persistence);
        var coordinator = CreateCoordinator(definition, services, new BackgroundTaskCoordinatorOptions(TimeSpan.FromMilliseconds(10)));

        Assert.Equal(BackgroundTaskRunRequestStatus.Accepted,
            (await coordinator.RequestManualRunAsync(definition.Id, CancellationToken.None)).Status);
        await WaitForCompletionAttemptsAsync(persistence);
        Assert.Equal(BackgroundTaskRunRequestStatus.Running,
            (await coordinator.RequestManualRunAsync(definition.Id, CancellationToken.None)).Status);
        Assert.Equal(1, Volatile.Read(ref executions));
        Assert.Equal("Running", (await persistence.GetAsync(definition.Id, CancellationToken.None))!.State);

        await coordinator.StopAsync(CancellationToken.None);
        Assert.Equal("Running", (await persistence.GetAsync(definition.Id, CancellationToken.None))!.State);
        await persistence.EnsureDefinitionsAsync([new BackgroundTaskDefinitionRecordDto
        {
                        Id = definition.Id,
                        Name = definition.Name,
                        DefaultIntervalMinutes = definition.DefaultIntervalMinutes
        }], DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Equal("Interrupted", (await persistence.GetAsync(definition.Id, CancellationToken.None))!.State);
    }

    private static ServiceProvider CreateServices(FakePersistence persistence)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton(persistence);
        services.AddScoped<IBackgroundTaskDatabaseService>(sp => sp.GetRequiredService<FakePersistence>());
        return services.BuildServiceProvider();
    }

    private static BackgroundTaskCoordinator CreateCoordinator(BackgroundTaskDefinition definition, ServiceProvider services,
        BackgroundTaskCoordinatorOptions? options = null) =>
        new([definition], services.GetRequiredService<IServiceScopeFactory>(), NullLogger<BackgroundTaskCoordinator>.Instance,
            TimeProvider.System, options ?? BackgroundTaskCoordinatorOptions.Default);

    private static async Task<BackgroundTaskRowDto> WaitForStateAsync(FakePersistence persistence, string state)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var row = await persistence.GetAsync("test-job", CancellationToken.None);
            if (row?.State == state)
                return row;
            await Task.Delay(10);
        }
        throw new TimeoutException($"Task did not reach {state}.");
    }

    private static async Task WaitForCompletionAttemptsAsync(FakePersistence persistence)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (persistence.CompletionAttempts > 0)
                return;
            await Task.Delay(10);
        }
        throw new TimeoutException("The coordinator did not attempt to persist completion.");
    }

    private sealed class FakePersistence : IBackgroundTaskDatabaseService
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, BackgroundTaskRowDto> _rows = new(StringComparer.Ordinal);
        /// <summary>
        /// Указывает, должен ли тестовый сервис завершать сохранение начала задачи ошибкой.
        /// </summary>
        public bool FailStart
        {
            get; init;
        }
        /// <summary>
        /// Указывает, должен ли тестовый сервис завершать все записи результата ошибкой.
        /// </summary>
        public bool FailEveryCompletion
        {
            get; init;
        }
        /// <summary>
        /// Число записей результата, которые тестовый сервис должен завершить ошибкой.
        /// </summary>
        public int FailCompletions
        {
            get; set;
        }
        /// <summary>
        /// Число попыток сохранить результат выполнения задачи.
        /// </summary>
        public int CompletionAttempts
        {
            get; private set;
        }

        /// <summary>
        /// Создаёт отсутствующие записи определений фоновых задач и обновляет зарегистрированные сведения.
        /// </summary>
        /// <param name="definitions">Определения зарегистрированных задач.</param>
        /// <param name="now">Текущее время для операции.</param>
        /// <param name="ct">Токен отмены операции.</param>
        /// <returns>Задача завершается после выполнения проверок теста.</returns>
        public Task EnsureDefinitionsAsync(IReadOnlyCollection<BackgroundTaskDefinitionRecordDto> definitions, DateTimeOffset now, CancellationToken ct)
        {
            lock (_sync)
                foreach (var definition in definitions)
                {
                    if (!_rows.TryGetValue(definition.Id, out var row))
                        _rows.Add(definition.Id, new BackgroundTaskRowDto
                        {
                            Id = definition.Id,
                            Name = definition.Name,
                            IntervalMinutes = definition.DefaultIntervalMinutes,
                            State = "Idle",
                            NextRunAt = now,
                            LastStartedAt = null,
                            LastCompletedAt = null,
                            LastResult = null,
                            LastError = null
                        });
                    else if (row.State == "Running")
                        _rows[definition.Id] = row with
                        {
                            State = "Interrupted",
                            LastError = "The previous process stopped while this task was running."
                        };
                }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BackgroundTaskRowDto>> ListAsync(CancellationToken ct)
        {
            lock (_sync)
                return Task.FromResult<IReadOnlyList<BackgroundTaskRowDto>>(_rows.Values.ToArray());
        }

        public Task<BackgroundTaskRowDto?> GetAsync(string id, CancellationToken ct)
        {
            lock (_sync)
                return Task.FromResult(_rows.GetValueOrDefault(id));
        }

        public Task<bool> UpdateScheduleAsync(string id, int intervalMinutes, DateTimeOffset now, CancellationToken ct)
        {
            lock (_sync)
            {
                if (!_rows.TryGetValue(id, out var row))
                    return Task.FromResult(false);
                var next = row.State == "Running" ? row.NextRunAt : now.AddMinutes(intervalMinutes);
                _rows[id] = row with
                {
                    IntervalMinutes = intervalMinutes,
                    NextRunAt = next
                };
                return Task.FromResult(true);
            }
        }

        public Task<bool> TryStartAsync(string id, DateTimeOffset startedAt, CancellationToken ct)
        {
            if (FailStart)
                throw new InvalidOperationException("storage unavailable");
            lock (_sync)
            {
                if (!_rows.TryGetValue(id, out var row) || row.State == "Running")
                    return Task.FromResult(false);
                _rows[id] = row with
                {
                    State = "Running",
                    LastStartedAt = startedAt,
                    LastError = null
                };
                return Task.FromResult(true);
            }
        }

        public Task<bool> CompleteAsync(string id, string state, DateTimeOffset completedAt, string? result, string? error, CancellationToken ct)
        {
            lock (_sync)
            {
                CompletionAttempts++;
                if (FailEveryCompletion || FailCompletions > 0)
                {
                    if (FailCompletions > 0)
                        FailCompletions--;
                    throw new InvalidOperationException("temporary storage failure");
                }
                if (!_rows.TryGetValue(id, out var row) || row.State != "Running")
                    return Task.FromResult(false);
                _rows[id] = row with
                {
                    State = state,
                    LastCompletedAt = completedAt,
                    LastResult = result,
                    LastError = error,
                    NextRunAt = completedAt.AddMinutes(row.IntervalMinutes)
                };
                return Task.FromResult(true);
            }
        }
    }
}
