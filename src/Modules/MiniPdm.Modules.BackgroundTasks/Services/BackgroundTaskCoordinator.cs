using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniPdm.Contracts.Modules.BackgroundTasks.DtoModels;
using MiniPdm.Modules.BackgroundTasks.Abstractions.Database;
using MiniPdm.Modules.BackgroundTasks.DtoModels;
using MiniPdm.Modules.BackgroundTasks.Abstractions;

namespace MiniPdm.Modules.BackgroundTasks.Services;

/// <summary>
/// Координирует зарегистрированные задачи, расписание и их выполнение независимо от HTTP-запросов.
/// </summary>
public sealed class BackgroundTaskCoordinator : IHostedService, IBackgroundTaskCoordinator
{
    private static readonly TimeSpan InitializationRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumPollDelay = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundTaskCoordinator> _logger;
    private readonly TimeProvider _clock;
    private readonly BackgroundTaskCoordinatorOptions _options;
    private readonly Dictionary<string, BackgroundTaskDefinition> _definitions;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _taskLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _persistenceLocks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);
    private readonly object _ownedTasksLock = new();
    private readonly Dictionary<long, Task> _ownedTasks = [];
    private readonly CancellationTokenSource _stopping = new();
    private long _nextRunId;
    private Task? _runner;
    private volatile bool _initialized;

    /// <summary>
    /// Создаёт координатор с набором зарегистрированных задач и зависимостями для их выполнения.
    /// </summary>
    /// <param name="definitions">Определения задач, доступных координатору.</param>
    /// <param name="scopeFactory">Фабрика областей зависимостей для выполнения и доступа к данным.</param>
    /// <param name="logger">Журнал для ошибок запуска и сохранения состояния.</param>
    /// <param name="clock">Поставщик текущего времени.</param>
    /// <param name="options">Параметры повторной записи результата.</param>
    public BackgroundTaskCoordinator(
        IEnumerable<BackgroundTaskDefinition> definitions,
        IServiceScopeFactory scopeFactory,
        ILogger<BackgroundTaskCoordinator> logger,
        TimeProvider clock,
        BackgroundTaskCoordinatorOptions options)
    {
        _definitions = definitions.ToDictionary(x => x.Id, StringComparer.Ordinal);
        _scopeFactory = scopeFactory;
        _logger = logger;
        _clock = clock;
        _options = options;
        foreach (var definition in _definitions.Values)
        {
            if (string.IsNullOrWhiteSpace(definition.Id) || definition.Id.Length > 128)
                throw new InvalidOperationException("Background task IDs must contain 1 to 128 characters.");
            if (definition.DefaultIntervalMinutes is < 1 or > 525600)
                throw new InvalidOperationException($"Background task '{definition.Id}' has an invalid default interval.");
            _taskLocks[definition.Id] = new SemaphoreSlim(1, 1);
            _persistenceLocks[definition.Id] = new SemaphoreSlim(1, 1);
        }
    }

    /// <summary>
    /// Запускает фоновый цикл планировщика.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены запуска службы.</param>
    /// <returns>Завершённая задача после старта цикла планировщика.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _runner = Task.Run(() => RunSchedulerAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Останавливает планировщик и ожидает завершения принадлежащих координатору запусков.
    /// </summary>
    /// <param name="cancellationToken">Токен, ограничивающий ожидание остановки.</param>
    /// <returns>Задача, завершающаяся после остановки или отмены ожидания.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        Signal();
        if (_runner is not null)
        {
            try
            {
                await _runner.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }

        Task[] owned;
        lock (_ownedTasksLock)
            owned = _ownedTasks.Values.ToArray();
        if (owned.Length > 0)
        {
            try
            {
                await Task.WhenAll(owned).WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BackgroundTaskDto>> ListAsync(CancellationToken ct)
    {
        await EnsureReadyAsync(ct);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var rows = await scope.ServiceProvider.GetRequiredService<IBackgroundTaskDatabaseService>().ListAsync(ct);
            return rows.Select(ToDto).ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not BackgroundTaskPersistenceUnavailableException)
        {
            throw Unavailable(ex);
        }
    }

    /// <inheritdoc />
    public async Task<BackgroundTaskDto?> UpdateScheduleAsync(string taskId, int intervalMinutes, CancellationToken ct)
    {
        if (!_definitions.ContainsKey(taskId))
            return null;
        await EnsureReadyAsync(ct);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var persistence = scope.ServiceProvider.GetRequiredService<IBackgroundTaskDatabaseService>();
            var persistenceLock = _persistenceLocks[taskId];
            await persistenceLock.WaitAsync(ct);
            try
            {
                if (!await persistence.UpdateScheduleAsync(taskId, intervalMinutes, _clock.GetUtcNow(), ct))
                    return null;
                Signal();
                var row = await persistence.GetAsync(taskId, ct);
                return row is null ? null : ToDto(row);
            }
            finally { persistenceLock.Release(); }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not BackgroundTaskPersistenceUnavailableException)
        {
            throw Unavailable(ex);
        }
    }

    /// <inheritdoc />
    public async Task<BackgroundTaskRunRequestResult> RequestManualRunAsync(string taskId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_definitions.TryGetValue(taskId, out var definition))
            return new(BackgroundTaskRunRequestStatus.NotFound);
        await EnsureReadyAsync(ct);
        if (_stopping.IsCancellationRequested)
            throw new BackgroundTaskPersistenceUnavailableException("The background task service is stopping.", new OperationCanceledException());
        try
        {
            return await TryLaunchAsync(definition, ct)
                ? new(BackgroundTaskRunRequestStatus.Accepted)
                : new(BackgroundTaskRunRequestStatus.Running);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not BackgroundTaskPersistenceUnavailableException)
        {
            throw Unavailable(ex);
        }
    }

    private async Task RunSchedulerAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnsureReadyAsync(stoppingToken);
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to initialize background task persistence; retrying in {Delay}.", InitializationRetryDelay);
                try
                {
                    await Task.Delay(InitializationRetryDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            }
        }

        // Run once immediately after startup; all later invocations follow persisted schedules.
        foreach (var definition in _definitions.Values)
            await TryLaunchScheduledAsync(definition, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<BackgroundTaskRowDto> rows;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                rows = await scope.ServiceProvider.GetRequiredService<IBackgroundTaskDatabaseService>().ListAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to read background task schedules; retrying in {Delay}.", InitializationRetryDelay);
                try
                {
                    await Task.Delay(InitializationRetryDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                continue;
            }

            var now = _clock.GetUtcNow();
            foreach (var row in rows.Where(x => x.State != "Running" && x.NextRunAt <= now))
                if (_definitions.TryGetValue(row.Id, out var definition))
                    await TryLaunchScheduledAsync(definition, stoppingToken);

            var next = rows.Where(x => x.State != "Running" && x.NextRunAt > now).MinBy(x => x.NextRunAt)?.NextRunAt;
            var delay = next is null ? MaximumPollDelay : next.Value - now;
            if (delay <= TimeSpan.Zero || delay > MaximumPollDelay)
                delay = MaximumPollDelay;
            try
            {
                await _wakeSignal.WaitAsync(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task TryLaunchScheduledAsync(BackgroundTaskDefinition definition, CancellationToken ct)
    {
        try
        {
            await TryLaunchAsync(definition, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogError(ex, "Could not start scheduled background task {TaskId}.", definition.Id); }
    }

    private async Task<bool> TryLaunchAsync(BackgroundTaskDefinition definition, CancellationToken ct)
    {
        var taskLock = _taskLocks[definition.Id];
        if (!await taskLock.WaitAsync(0, ct))
            return false;
        var started = false;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var persistence = scope.ServiceProvider.GetRequiredService<IBackgroundTaskDatabaseService>();
            var persistenceLock = _persistenceLocks[definition.Id];
            using var startTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await persistenceLock.WaitAsync(startTimeout.Token);
            try
            {
                started = await persistence.TryStartAsync(definition.Id, _clock.GetUtcNow(), startTimeout.Token);
            }
            finally { persistenceLock.Release(); }
            if (!started)
                return false;
        }
        finally
        {
            if (!started)
                taskLock.Release();
        }

        var runId = Interlocked.Increment(ref _nextRunId);
        lock (_ownedTasksLock)
            _ownedTasks[runId] = Task.Run(() => RunOwnedTaskAsync(runId, definition, taskLock, _stopping.Token), CancellationToken.None);
        Signal();
        return true;
    }

    private async Task RunOwnedTaskAsync(long runId, BackgroundTaskDefinition definition, SemaphoreSlim taskLock, CancellationToken ct)
    {
        string state;
        string? result = null;
        string? error = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var execution = await definition.ExecuteAsync(scope.ServiceProvider, ct);
            result = execution.Summary;
            error = execution.Errors.Count == 0 ? null : string.Join(Environment.NewLine, execution.Errors);
            state = execution.Errors.Count == 0 ? "Succeeded" : "PartiallySucceeded";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            state = "Interrupted";
            error = "The task was interrupted while the service was stopping.";
        }
        catch (Exception ex)
        {
            state = "Failed";
            error = "The task failed; see server logs for details.";
            _logger.LogError(ex, "Background task {TaskId} failed.", definition.Id);
        }
        var completedAt = _clock.GetUtcNow();

        try
        {
            while (true)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var persistenceLock = _persistenceLocks[definition.Id];
                    using var saveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await persistenceLock.WaitAsync(saveTimeout.Token);
                    bool saved;
                    try
                    {
                        saved = await scope.ServiceProvider.GetRequiredService<IBackgroundTaskDatabaseService>()
                            .CompleteAsync(definition.Id, state, completedAt, result, error, saveTimeout.Token);
                    }
                    finally { persistenceLock.Release(); }
                    if (!saved)
                        _logger.LogWarning("Completion for background task {TaskId} was not saved because its row was no longer Running.", definition.Id);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unable to persist completion for background task {TaskId}; retrying the status write without rerunning the task.", definition.Id);
                    try
                    {
                        await Task.Delay(_options.CompletionWriteRetryDelay, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                }
            }
        }
        finally
        {
            taskLock.Release();
            lock (_ownedTasksLock)
                _ownedTasks.Remove(runId);
            Signal();
        }
    }

    private async Task EnsureReadyAsync(CancellationToken ct)
    {
        if (_initialized)
            return;
        await _initializationLock.WaitAsync(ct);
        try
        {
            if (_initialized)
                return;
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IBackgroundTaskDatabaseService>()
                .EnsureDefinitionsAsync(_definitions.Values.Select(x => new BackgroundTaskDefinitionRecordDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    DefaultIntervalMinutes = x.DefaultIntervalMinutes
                }).ToArray(), _clock.GetUtcNow(), ct);
            _initialized = true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to ensure background task definitions in storage.");
            throw Unavailable(ex);
        }
        finally { _initializationLock.Release(); }
    }

    private static BackgroundTaskDto ToDto(BackgroundTaskRowDto row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            IntervalMinutes = row.IntervalMinutes,
            State = row.State,
            NextRunAt = row.NextRunAt,
            LastStartedAt = row.LastStartedAt,
            LastCompletedAt = row.LastCompletedAt,
            LastResult = row.LastResult,
            LastError = row.LastError
        };

    private static BackgroundTaskPersistenceUnavailableException Unavailable(Exception exception) =>
        new("Background task persistence is temporarily unavailable.", exception);

    private void Signal()
    {
        try
        {
            _wakeSignal.Release();
        }
        catch (SemaphoreFullException) { }
    }
}
