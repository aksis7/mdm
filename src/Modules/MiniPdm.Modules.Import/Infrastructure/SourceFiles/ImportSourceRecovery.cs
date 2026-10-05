using Microsoft.Extensions.Options;
using MiniPdm.Modules.Import.Abstractions;
using MiniPdm.Modules.Import.Abstractions.Database;
using MiniPdm.Modules.Import.DtoModels.Database;

namespace MiniPdm.Modules.Import.Infrastructure.SourceFiles;

/// <summary>
/// Сверяет сохранённые и заброшенные каталоги с журналом импорта и арендой загрузки.
/// </summary>
/// <param name="options">Настройки корня файлового хранилища.</param>
/// <param name="fileStorage">Операции восстановления временных файловых каталогов.</param>
/// <param name="sourceStorage">Удаление сохранённых источников импорта.</param>
/// <param name="persistence">Сервис выяснения исхода операций из журнала.</param>
public sealed class ImportSourceRecovery(
    IOptions<ImportStorageOptions> options,
    FileImportStorage fileStorage,
    IImportSourceStorage sourceStorage,
    IImportDatabaseService persistence)
{
    private readonly string _importsRoot = Path.Combine(Path.GetFullPath(string.IsNullOrWhiteSpace(options.Value.DataRoot)
        ? Path.Combine(AppContext.BaseDirectory, "data") : options.Value.DataRoot), "imports");

    /// <summary>
    /// Удаляет подтверждённо заброшенные каталоги и возвращает их количество.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены обхода.</param>
    /// <returns>Количество удалённых каталогов.</returns>
    public async Task<int> RecoverAsync(CancellationToken cancellationToken = default) =>
        (await RecoverDetailedAsync(cancellationToken)).RemovedCount;

    /// <summary>
    /// Выполняет восстановление и возвращает сведения об удалениях и ошибках.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены обхода.</param>
    /// <returns>Подробный итог сверки каталогов импорта.</returns>
    public async Task<ImportSourceRecoveryResult> RecoverDetailedAsync(CancellationToken cancellationToken = default)
    {
        var removed = 0;
        var errors = new List<string>();

        foreach (var folder in EnumerateDirectories(_importsRoot, "imports", errors))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(folder);
            if (Guid.TryParseExact(name, "D", out var importId))
            {
                ImportPersistenceResultDto state;
                try
                {
                    state = await persistence.ResolveAsync(importId, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch
                {
                    errors.Add($"Database reconciliation failed for imports/{name}; the folder was retained.");
                    continue;
                }

                if (state.State == ImportCommitState.Completed)
                    continue;
                if (state.State == ImportCommitState.Unknown)
                {
                    errors.Add($"The database outcome is unknown for imports/{name}; the folder was retained.");
                    continue;
                }

                try
                {
                    if (await persistence.CompensateIfRolledBackAsync(importId,
                            ct => sourceStorage.CompensateAsync(importId, ct), cancellationToken))
                        removed++;
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch
                {
                    errors.Add($"Could not compensate imports/{name}; the folder was retained.");
                }
                continue;
            }

            if (!TryParsePromotionName(name, out var promotionId))
                continue;
            ImportPersistenceResultDto promotionState;
            try
            {
                promotionState = await persistence.ResolveAsync(promotionId, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                errors.Add($"Database reconciliation failed for promotion folder imports/{name}; the folder was retained.");
                continue;
            }
            if (promotionState.State == ImportCommitState.Completed)
                continue;
            if (promotionState.State == ImportCommitState.Unknown)
            {
                errors.Add($"The database outcome is unknown for promotion folder imports/{name}; the folder was retained.");
                continue;
            }

            var folderRemoved = false;
            try
            {
                var mayCompensate = await persistence.CompensateIfRolledBackAsync(promotionId,
                    async ct => { folderRemoved = await fileStorage.RecoverAbandonedPromotionAsync(promotionId, folder, ct); },
                    cancellationToken);
                if (mayCompensate && folderRemoved)
                    removed++;
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                errors.Add($"Could not recover promotion folder imports/{name}; the folder was retained.");
            }
        }

        var uploadsRoot = Path.Combine(Path.GetDirectoryName(_importsRoot)!, "uploads");
        foreach (var importFolder in EnumerateDirectories(uploadsRoot, "uploads", errors))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var importName = Path.GetFileName(importFolder);
            if (!Guid.TryParseExact(importName, "D", out _))
                continue;

            foreach (var attempt in EnumerateDirectories(importFolder, $"uploads/{importName}", errors))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attemptName = Path.GetFileName(attempt);
                if (!Guid.TryParseExact(attemptName, "N", out _))
                    continue;

                AbandonedUploadRecoveryStatus status;
                try
                {
                    status = fileStorage.RecoverAbandonedUploadAttemptDetailed(attempt);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch
                {
                    errors.Add($"Could not recover upload folder uploads/{importName}/{attemptName}; the folder was retained.");
                    continue;
                }

                if (status == AbandonedUploadRecoveryStatus.Removed)
                    removed++;
                else if (status == AbandonedUploadRecoveryStatus.Failed)
                    errors.Add($"Could not recover upload folder uploads/{importName}/{attemptName}; the folder was retained.");
                // An absent marker or an active lease is a normal skip.
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new ImportSourceRecoveryResult(removed, errors);
    }

    private static string[] EnumerateDirectories(string path, string displayPath, ICollection<string> errors)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch (DirectoryNotFoundException) { return []; }
        catch (IOException)
        {
            errors.Add($"Could not enumerate {displayPath}; its contents were left untouched.");
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            errors.Add($"Access was denied while enumerating {displayPath}; its contents were left untouched.");
            return [];
        }
        catch (System.Security.SecurityException)
        {
            errors.Add($"Access was denied while enumerating {displayPath}; its contents were left untouched.");
            return [];
        }
    }

    private static bool TryParsePromotionName(string name, out Guid importId)
    {
        importId = Guid.Empty;
        if (!name.StartsWith(".", StringComparison.Ordinal) || !name.EndsWith(".promoting", StringComparison.Ordinal))
            return false;
        var parts = name.Substring(1, name.Length - 1 - ".promoting".Length).Split('.');
        return parts.Length == 2 && Guid.TryParseExact(parts[0], "D", out importId) && Guid.TryParseExact(parts[1], "N", out _);
    }
}
