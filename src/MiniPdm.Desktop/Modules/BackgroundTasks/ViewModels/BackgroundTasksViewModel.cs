using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using MiniPdm.Contracts.Modules.BackgroundTasks.DtoModels;
using MiniPdm.Desktop.Services;
using MiniPdm.Desktop.Services.Abstractions;
using MiniPdm.Desktop.ViewModels;

namespace MiniPdm.Desktop.Modules.BackgroundTasks.ViewModels;

/// <summary>
/// Предоставляет состояние и команды управления фоновыми задачами сервера.
/// </summary>
public sealed class BackgroundTasksViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IPdmApiClient _client;
    private readonly AsyncCommand _refreshCommand;
    private readonly AsyncCommand _saveScheduleCommand;
    private readonly AsyncCommand _runCommand;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _polling;
    private BackgroundTaskDto? _selectedTask;
    private string _intervalMinutesText = string.Empty;
    private string _statusText = string.Empty;
    private bool _isBusy;
    private bool _disposed;

    /// <summary>
    /// Создаёт модель представления и сразу запрашивает список задач.
    /// </summary>
    /// <param name="client">Клиент для чтения и изменения задач API.</param>
    /// <param name="pollInterval">Пауза между проверками запущенной задачи; по умолчанию одна секунда.</param>
    /// <param name="maxPollCount">Максимальное количество проверок после запуска.</param>
    public BackgroundTasksViewModel(IPdmApiClient client, TimeSpan? pollInterval = null, int maxPollCount = 20)
    {
        _client = client;
        PollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        MaxPollCount = Math.Max(0, maxPollCount);
        _refreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy, HandleError);
        _saveScheduleCommand = new AsyncCommand(SaveScheduleAsync, CanSaveSchedule, HandleError);
        _runCommand = new AsyncCommand(RunAsync, () => !IsBusy && SelectedTask is not null && !IsActive(SelectedTask.State), HandleError);
        RefreshCommand = _refreshCommand;
        SaveScheduleCommand = _saveScheduleCommand;
        RunCommand = _runCommand;
        _ = RefreshAsync();
    }

    /// <summary>
    /// Возникает при изменении свойства модели представления.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Возвращает задачи, отображаемые в списке.
    /// </summary>
    public ObservableCollection<BackgroundTaskDto> Tasks { get; } = [];

    /// <summary>
    /// Возвращает команду загрузки списка задач.
    /// </summary>
    public AsyncCommand RefreshCommand
    {
        get;
    }

    /// <summary>
    /// Возвращает команду сохранения расписания выбранной задачи.
    /// </summary>
    public AsyncCommand SaveScheduleCommand
    {
        get;
    }

    /// <summary>
    /// Возвращает команду немедленного запуска выбранной задачи.
    /// </summary>
    public AsyncCommand RunCommand
    {
        get;
    }

    /// <summary>
    /// Возвращает интервал между проверками состояния выполняемой задачи.
    /// </summary>
    public TimeSpan PollInterval
    {
        get;
    }

    /// <summary>
    /// Возвращает максимальное количество проверок состояния после запуска.
    /// </summary>
    public int MaxPollCount
    {
        get;
    }

    /// <summary>
    /// Получает или задаёт выбранную задачу и синхронизирует текст интервала с её расписанием.
    /// </summary>
    public BackgroundTaskDto? SelectedTask
    {
        get => _selectedTask;
        set
        {
            if (!Set(ref _selectedTask, value))
                return;
            IntervalMinutesText = value?.IntervalMinutes.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            RefreshCommands();
        }
    }

    /// <summary>
    /// Получает или задаёт редактируемый интервал расписания в минутах.
    /// </summary>
    public string IntervalMinutesText
    {
        get => _intervalMinutesText;
        set
        {
            if (Set(ref _intervalMinutesText, value))
                RefreshCommands();
        }
    }

    /// <summary>
    /// Возвращает текст текущего состояния операций с задачами.
    /// </summary>
    public string StatusText
    {
        get => _statusText; private set => Set(ref _statusText, value);
    }

    /// <summary>
    /// Показывает, выполняется ли сетевое действие.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy; private set
        {
            if (Set(ref _isBusy, value))
                RefreshCommands();
        }
    }

    /// <summary>
    /// Показывает, можно ли редактировать расписание выбранной задачи.
    /// </summary>
    public bool IsScheduleEditable => !IsBusy && SelectedTask is not null;

    /// <summary>
    /// Загружает актуальный список задач и сохраняет выбор по идентификатору.
    /// </summary>
    /// <returns>Асинхронная операция загрузки.</returns>
    public async Task RefreshAsync()
    {
        await RunBusyAsync(async () =>
        {
            var selectedId = SelectedTask?.Id;
            var rows = await _client.GetBackgroundTasksAsync(_lifetime.Token);
            Tasks.Clear();
            foreach (var row in rows)
                Tasks.Add(row);
            SelectedTask = selectedId is null ? Tasks.FirstOrDefault() : Tasks.FirstOrDefault(x => x.Id == selectedId) ?? Tasks.FirstOrDefault();
            StatusText = $"Загружено задач: {Tasks.Count}.";
        });
    }

    /// <summary>
    /// Отменяет текущую последовательность опроса запущенной задачи.
    /// </summary>
    public void CancelActivePolling() => _polling?.Cancel();

    /// <summary>
    /// Отменяет выполняемые запросы и освобождает связанные ресурсы.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _polling?.Cancel();
        _polling?.Dispose();
        _polling = null;
    }

    private bool CanSaveSchedule() => !IsBusy && SelectedTask is not null &&
        int.TryParse(IntervalMinutesText, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) && minutes is >= 1 and <= 525600;

    private async Task SaveScheduleAsync()
    {
        if (SelectedTask is null || !int.TryParse(IntervalMinutesText, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
            return;
        var id = SelectedTask.Id;
        await RunBusyAsync(async () =>
        {
            var updated = await _client.UpdateBackgroundTaskScheduleAsync(id, new UpdateBackgroundTaskScheduleRequestDto
            {
                IntervalMinutes = minutes
            }, _lifetime.Token);
            Upsert(updated);
            SelectedTask = updated;
            StatusText = "Расписание сохранено.";
        });
    }

    private async Task RunAsync()
    {
        if (SelectedTask is null)
            return;
        var id = SelectedTask.Id;
        var lifetimeToken = _lifetime.Token;
        await RunBusyAsync(async () =>
        {
            await _client.RunBackgroundTaskAsync(id, lifetimeToken);
            StatusText = "Задача запущена.";
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
            _polling = cts;
            try
            {
                for (var attempt = 0; attempt < MaxPollCount; attempt++)
                {
                    await Task.Delay(PollInterval, cts.Token);
                    var rows = await _client.GetBackgroundTasksAsync(cts.Token);
                    foreach (var row in rows)
                        Upsert(row);
                    var current = Tasks.FirstOrDefault(x => x.Id == id);
                    if (current is not null)
                    {
                        SelectedTask = current;
                        if (!IsActive(current.State))
                        {
                            StatusText = current.LastError is { Length: > 0 } error ? error : current.LastResult ?? "Задача завершена.";
                            return;
                        }
                    }
                }
                var final = Tasks.FirstOrDefault(x => x.Id == id);
                if (final is not null)
                    SelectedTask = final;
                StatusText = "Задача ещё выполняется. Обновите состояние позже.";
            }
            finally { if (ReferenceEquals(_polling, cts)) _polling = null; }
        });
    }

    private static bool IsActive(string state) => state.Equals("Queued", StringComparison.OrdinalIgnoreCase)
        || state.Equals("Running", StringComparison.OrdinalIgnoreCase);

    private void Upsert(BackgroundTaskDto task)
    {
        var index = -1;
        for (var i = 0; i < Tasks.Count; i++)
            if (Tasks[i].Id == task.Id)
            {
                index = i;
                break;
            }
        if (index < 0)
            Tasks.Add(task);
        else
            Tasks[index] = task;
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (PdmApiException ex) { StatusText = ex.Message; }
        catch (Exception ex) { StatusText = ex.Message; }
        finally { IsBusy = false; }
    }

    private void HandleError(Exception ex) => StatusText = ex.Message;

    private void RefreshCommands()
    {
        _refreshCommand.Refresh();
        _saveScheduleCommand.Refresh();
        _runCommand.Refresh();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name is nameof(IsBusy) or nameof(SelectedTask))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsScheduleEditable)));
        return true;
    }
}
