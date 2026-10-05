using System.Windows.Input;
using System.Diagnostics;

namespace MiniPdm.Desktop.ViewModels;

/// <summary>
/// Адаптирует асинхронное действие к команде интерфейса и не допускает повторный запуск.
/// </summary>
/// <param name="execute">Асинхронное действие команды.</param>
/// <param name="canExecute">Проверка возможности запуска; если не задана, команда доступна вне выполнения.</param>
/// <param name="onError">Обработчик исключения действия; если не задан, исключение передаётся вызывающему коду.</param>
public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null) : ICommand
{
    private bool _running;

    /// <summary>
    /// Возникает при изменении доступности команды.
    /// </summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Проверяет, может ли команда начать выполнение.
    /// </summary>
    /// <param name="parameter">Параметр команды; действие его не использует.</param>
    /// <returns><see langword="true"/>, если команда доступна и ещё не выполняется.</returns>
    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    /// <summary>
    /// Запускает асинхронное действие по контракту <see cref="ICommand"/>.
    /// </summary>
    /// <param name="parameter">Параметр команды; действие его не использует.</param>
    public async void Execute(object? parameter)
    {
        try
        {
            await ExecuteAsync(parameter);
        }
        catch (Exception ex) { Trace.TraceError("Unhandled command error: {0}", ex); }
    }

    /// <summary>
    /// Выполняет команду асинхронно и передаёт ошибку указанному обработчику.
    /// </summary>
    /// <param name="parameter">Параметр команды; действие его не использует.</param>
    /// <returns>Асинхронная операция выполнения.</returns>
    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter))
            return;
        _running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await execute();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (onError is null)
                throw;
            onError(ex);
        }
        finally
        {
            _running = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Уведомляет интерфейс о возможном изменении доступности команды.
    /// </summary>
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Представляет синхронное действие с проверкой возможности запуска.
/// </summary>
/// <param name="execute">Синхронное действие команды.</param>
/// <param name="canExecute">Проверка доступности команды; если не задана, команда доступна.</param>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    /// <summary>
    /// Возникает при изменении доступности команды.
    /// </summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Проверяет, доступна ли команда.
    /// </summary>
    /// <param name="parameter">Параметр команды; действие его не использует.</param>
    /// <returns><see langword="true"/>, если проверка доступности разрешает запуск.</returns>
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    /// <summary>
    /// Выполняет действие, если команда доступна.
    /// </summary>
    /// <param name="parameter">Параметр команды; действие его не использует.</param>
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
            execute();
    }

    /// <summary>
    /// Уведомляет интерфейс о возможном изменении доступности команды.
    /// </summary>
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
