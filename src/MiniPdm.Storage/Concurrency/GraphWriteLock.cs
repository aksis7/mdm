namespace MiniPdm.Storage.Concurrency;

/// <summary>
/// Задаёт ключ блокировки транзакций для команд, изменяющих действующий граф состава.
/// </summary>
public static class GraphWriteLock
{
    /// <summary>
    ///     Ключ advisory-блокировки PostgreSQL, общий для операций изменения действующего графа состава.
    /// </summary>
    public const long AdvisoryLockKey = 0x50444D4752415048;
}
