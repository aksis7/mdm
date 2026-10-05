using Microsoft.EntityFrameworkCore;
using MiniPdm.Modules.BackgroundTasks.Abstractions.Database;
using MiniPdm.Modules.BackgroundTasks.DtoModels;
using MiniPdm.Storage;
using MiniPdm.Storage.Entities;

namespace MiniPdm.Modules.BackgroundTasks.Services.Database;

/// <summary>
/// Реализует хранение состояний фоновых задач через контекст базы данных.
/// </summary>
/// <param name="context">Контекст базы данных PDM.</param>
public sealed class BackgroundTaskDatabaseService(PdmDbContext context) : IBackgroundTaskDatabaseService
{
    /// <inheritdoc />
    public async Task EnsureDefinitionsAsync(IReadOnlyCollection<BackgroundTaskDefinitionRecordDto> definitions, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await context.BackgroundTasks.ToListAsync(ct);
        var byId = existing.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (!byId.TryGetValue(definition.Id, out var row))
            {
                context.BackgroundTasks.Add(new BackgroundTask
                {
                    Id = definition.Id,
                    Name = definition.Name,
                    IntervalMinutes = definition.DefaultIntervalMinutes,
                    State = "Idle",
                    NextRunAt = now
                });
                continue;
            }

            row.Name = definition.Name;
            if (row.State == "Running")
            {
                row.State = "Interrupted";
                row.LastError = "The previous process stopped while this task was running.";
            }
            row.NextRunAt ??= now;
        }
        await context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BackgroundTaskRowDto>> ListAsync(CancellationToken ct) =>
        await context.BackgroundTasks.AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(ToRow())
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<BackgroundTaskRowDto?> GetAsync(string id, CancellationToken ct) =>
        await context.BackgroundTasks.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToRow())
            .SingleOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<bool> UpdateScheduleAsync(string id, int intervalMinutes, DateTimeOffset now, CancellationToken ct)
    {
        ValidateInterval(intervalMinutes);
        var task = await context.BackgroundTasks.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (task is null)
            return false;
        task.IntervalMinutes = intervalMinutes;
        if (task.State != "Running")
            task.NextRunAt = now.AddMinutes(intervalMinutes);
        await context.SaveChangesAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> TryStartAsync(string id, DateTimeOffset startedAt, CancellationToken ct)
    {
        var task = await context.BackgroundTasks.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (task is null || task.State == "Running")
            return false;
        task.State = "Running";
        task.LastStartedAt = startedAt;
        task.LastError = null;
        await context.SaveChangesAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAsync(string id, string state, DateTimeOffset completedAt, string? result, string? error, CancellationToken ct)
    {
        if (state is not ("Succeeded" or "PartiallySucceeded" or "Failed" or "Interrupted"))
            throw new ArgumentOutOfRangeException(nameof(state), "A completed background task must have a terminal state.");
        var task = await context.BackgroundTasks.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (task is null || task.State != "Running")
            return false;
        task.State = state;
        task.LastCompletedAt = completedAt;
        task.LastResult = result;
        task.LastError = error;
        task.NextRunAt = completedAt.AddMinutes(task.IntervalMinutes);
        await context.SaveChangesAsync(ct);
        return true;
    }

    private static System.Linq.Expressions.Expression<Func<BackgroundTask, BackgroundTaskRowDto>> ToRow() =>
        x => new BackgroundTaskRowDto
        {
            Id = x.Id,
            Name = x.Name,
            IntervalMinutes = x.IntervalMinutes,
            State = x.State,
            NextRunAt = x.NextRunAt,
            LastStartedAt = x.LastStartedAt,
            LastCompletedAt = x.LastCompletedAt,
            LastResult = x.LastResult,
            LastError = x.LastError
        };

    private static void ValidateInterval(int intervalMinutes)
    {
        if (intervalMinutes is < 1 or > 525600)
            throw new ArgumentOutOfRangeException(nameof(intervalMinutes), "Interval must be between 1 and 525600 minutes.");
    }
}
