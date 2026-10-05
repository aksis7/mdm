namespace MiniPdm.Modules.Import.Infrastructure.SourceFiles;

/// <summary>
/// Итог сверки каталогов источников импорта с журналом базы данных.
/// </summary>
public sealed record ImportSourceRecoveryResult(int RemovedCount, IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Число удалённых заброшенных каталогов.
    /// </summary>
    public int RemovedCount { get; init; } = RemovedCount;

    /// <summary>
    /// Ошибки, из-за которых отдельные каталоги были оставлены.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Errors;
}
