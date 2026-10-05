using Resources = MiniPdm.Common.Resources;
using Microsoft.Extensions.Options;
using MiniPdm.Modules.Import.Abstractions;
using MiniPdm.Modules.Import.Abstractions.Cad;
using MiniPdm.Modules.Import.DtoModels.Cad;

namespace MiniPdm.Modules.Import.Infrastructure.SourceFiles;

/// <summary>
/// Сохраняет загружаемые файлы во временных и долговременных каталогах на диске.
/// </summary>
/// <param name="options">Настройки корневого каталога и лимитов загрузки.</param>
public sealed class FileImportStorage(IOptions<ImportStorageOptions> options) : IImportSourceStorage, IImportUploadStorage
{
    private readonly string _root = Path.GetFullPath(string.IsNullOrWhiteSpace(options.Value.DataRoot)
        ? Path.Combine(AppContext.BaseDirectory, "data") : options.Value.DataRoot);

    /// <inheritdoc />
    public async Task<IImportUploadAttempt> StageAsync(Guid importId, IReadOnlyList<ImportUploadFile> files, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(files);
        var limits = options.Value;
        ValidateLimits(limits);
        if (files.Count == 0 || files.Count > limits.MaxFiles)
            throw new ImportUploadValidationException(string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.InputLogicException.CadUploadFileCount, limits.MaxFiles));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (file is null || !IsSafeCadName(file.FileName))
                throw new ImportUploadValidationException(Resources.InputLogicException.CadUploadSafeFile);
            if (!names.Add(file.FileName))
                throw new ImportUploadValidationException(string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.InputLogicException.CadUploadDuplicate, file.FileName));
        }

        var attemptPath = Path.Combine(_root, "uploads", importId.ToString("D"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(attemptPath);
        FileStream? attemptLease = null;
        try
        {
            attemptLease = new FileStream(Path.Combine(attemptPath, ".active"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1);
            long total = 0;
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var destination = Path.Combine(attemptPath, file.FileName);
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[81920];
                long fileBytes = 0;
                while (true)
                {
                    var read = await file.Content.ReadAsync(buffer, ct);
                    if (read == 0)
                        break;
                    fileBytes += read;
                    total += read;
                    if (fileBytes > limits.MaxFileBytes || total > limits.MaxTotalBytes)
                        throw new ImportUploadValidationException(Resources.InputLogicException.CadUploadExceeded);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            return new UploadAttempt(attemptPath, attemptLease);
        }
        catch
        {
            attemptLease?.Dispose();
            TryDeleteDirectory(attemptPath);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task PromoteAsync(Guid importId, CadSourceDescriptorDto source, IReadOnlyCollection<string> acceptedFiles, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(acceptedFiles);
        ct.ThrowIfCancellationRequested();
        if (!string.Equals(source.Kind, "file-json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only file-json source folders can be promoted.", nameof(source));
        var sourceRoot = Path.GetFullPath(source.Location);
        var destination = Path.Combine(_root, "imports", importId.ToString("D"));
        var importsRoot = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(importsRoot);
        var staging = Path.Combine(importsRoot, $".{importId:D}.{Guid.NewGuid():N}.promoting");
        if (Directory.Exists(destination))
            throw new IOException("Import source files have already been promoted.");
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var fileName in acceptedFiles.Distinct(StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (!IsSafeCadName(fileName))
                    throw new InvalidDataException("Accepted CAD file name is invalid.");
                var sourceFile = Path.GetFullPath(Path.Combine(sourceRoot, fileName));
                if (!IsWithin(sourceRoot, sourceFile) || !File.Exists(sourceFile))
                    throw new FileNotFoundException("Accepted CAD file was not found in its source folder.");
                if ((File.GetAttributes(sourceFile) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Symbolic links are not accepted as CAD files.");
                var targetFile = Path.Combine(staging, fileName);
                await using var input = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var output = new FileStream(targetFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await input.CopyToAsync(output, ct);
            }
            // Rename is atomic on the same filesystem and fails if another promotion won the race.
            Directory.Move(staging, destination);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    /// <inheritdoc />
    public string GetSourceReference(Guid importId, string fileName)
    {
        if (!IsSafeCadName(fileName))
            throw new ArgumentException("A safe CAD file name is required.", nameof(fileName));
        return Path.Combine(_root, "imports", importId.ToString("D"), fileName);
    }

    /// <inheritdoc />
    public Task CompensateAsync(Guid importId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = Path.Combine(_root, "imports", importId.ToString("D"));
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        return Task.CompletedTask;
    }

    internal bool RecoverAbandonedUploadAttempt(string path) =>
        RecoverAbandonedUploadAttemptDetailed(path) == AbandonedUploadRecoveryStatus.Removed;

    internal AbandonedUploadRecoveryStatus RecoverAbandonedUploadAttemptDetailed(string path)
    {
        FileAttributes attemptAttributes;
        try
        {
            attemptAttributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException) { return AbandonedUploadRecoveryStatus.Skipped; }
        catch (DirectoryNotFoundException) { return AbandonedUploadRecoveryStatus.Skipped; }
        catch (IOException) { return AbandonedUploadRecoveryStatus.Failed; }
        catch (UnauthorizedAccessException) { return AbandonedUploadRecoveryStatus.Failed; }
        catch (System.Security.SecurityException) { return AbandonedUploadRecoveryStatus.Failed; }
        if ((attemptAttributes & FileAttributes.Directory) == 0)
            return AbandonedUploadRecoveryStatus.Skipped;
        var uploadsRoot = Path.Combine(_root, "uploads");
        var importFolder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (importFolder is null || !IsDirectChild(uploadsRoot, importFolder) ||
            !Guid.TryParseExact(Path.GetFileName(importFolder), "D", out _) ||
            !Guid.TryParseExact(Path.GetFileName(path), "N", out _))
            return AbandonedUploadRecoveryStatus.Skipped;
        var marker = Path.Combine(path, ".active");
        try
        {
            _ = File.GetAttributes(marker);
        }
        catch (FileNotFoundException) { return AbandonedUploadRecoveryStatus.Skipped; }
        catch (DirectoryNotFoundException) { return AbandonedUploadRecoveryStatus.Skipped; }
        catch (IOException) { return AbandonedUploadRecoveryStatus.Failed; }
        catch (UnauthorizedAccessException) { return AbandonedUploadRecoveryStatus.Failed; }
        catch (System.Security.SecurityException) { return AbandonedUploadRecoveryStatus.Failed; }

        FileStream lease;
        try
        {
            lease = new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete);
        }
        catch (IOException) { return AbandonedUploadRecoveryStatus.ActiveLease; }
        catch (UnauthorizedAccessException) { return AbandonedUploadRecoveryStatus.Failed; }

        using (lease)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return AbandonedUploadRecoveryStatus.Removed;
            }
            catch (DirectoryNotFoundException) { return AbandonedUploadRecoveryStatus.Skipped; }
            catch (IOException) { return AbandonedUploadRecoveryStatus.Failed; }
            catch (UnauthorizedAccessException) { return AbandonedUploadRecoveryStatus.Failed; }
            catch (System.Security.SecurityException) { return AbandonedUploadRecoveryStatus.Failed; }
        }
    }

    internal Task<bool> RecoverAbandonedPromotionAsync(Guid importId, string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var importsRoot = Path.Combine(_root, "imports");
        var expectedPrefix = $".{importId:D}.";
        if (!IsDirectChild(importsRoot, path) || !Path.GetFileName(path).StartsWith(expectedPrefix, StringComparison.Ordinal) || !Path.GetFileName(path).EndsWith(".promoting", StringComparison.Ordinal))
            throw new InvalidOperationException("Recovery path is outside the import root.");
        try
        {
            _ = File.GetAttributes(path);
        }
        catch (FileNotFoundException) { return Task.FromResult(false); }
        catch (DirectoryNotFoundException) { return Task.FromResult(false); }
        Directory.Delete(path, recursive: true);
        return Task.FromResult(true);
    }

    private static bool IsSafeCadName(string? name) => !string.IsNullOrWhiteSpace(name) &&
        name is not "." and not ".." && name == Path.GetFileName(name) && !name.Contains('/') && !name.Contains('\\') &&
        !name.Any(char.IsControl) && (Path.GetExtension(name).Equals(".a3d", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(name).Equals(".m3d", StringComparison.OrdinalIgnoreCase));

    internal static bool HasValidLimits(ImportStorageOptions limits) =>
        limits.MaxFiles is > 0 and <= ImportStorageOptions.HardMaxFiles &&
        limits.MaxFileBytes is > 0 and <= ImportStorageOptions.HardMaxFileBytes &&
        limits.MaxTotalBytes is > 0 and <= ImportStorageOptions.HardMaxTotalBytes &&
        limits.MaxFileBytes <= limits.MaxTotalBytes;

    private static void ValidateLimits(ImportStorageOptions limits)
    {
        if (!HasValidLimits(limits))
            throw new InvalidOperationException("Import upload limits must be positive and cannot exceed the configured hard limits.");
    }

    private static bool IsWithin(string root, string path)
    {
        var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool IsDirectChild(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), fullRoot,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Представляет временные файлы одной попытки загрузки и удаляет их при освобождении.
    /// </summary>
    /// <param name="path">Каталог временной попытки.</param>
    /// <param name="lease">Блокировка, удерживающая каталог от восстановления до конца обработки.</param>
    private sealed class UploadAttempt(string path, FileStream lease) : IImportUploadAttempt
    {
        private bool _disposed;
        /// <summary>
        /// Описатель временного источника с загруженными файлами.
        /// </summary>
        public CadSourceDescriptorDto SourceDescriptor
        {
            get;
        } = new()
        {
            Kind = "file-json",
            Location = path
        };
        /// <summary>
        /// Освобождает блокировку и удаляет временный каталог попытки.
        /// </summary>
        /// <returns>Завершённая задача освобождения.</returns>
        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                lease.Dispose();
                TryDeleteDirectory(path);
                _disposed = true;
            }
            return ValueTask.CompletedTask;
        }
    }
}

internal enum AbandonedUploadRecoveryStatus
{
    /// <summary>
    /// Каталог отсутствует или не соответствует критериям восстановления.
    /// </summary>
    Skipped,
    /// <summary>
    /// Каталог используется активной попыткой загрузки.
    /// </summary>
    ActiveLease,
    /// <summary>
    /// Заброшенный каталог удалён.
    /// </summary>
    Removed,
    /// <summary>
    /// Каталог не удалось проверить или удалить.
    /// </summary>
    Failed
}

/// <summary>
/// Загрузка отклонена из-за недопустимого имени, количества или размера файлов.
/// </summary>
/// <param name="message">Причина отклонения загрузки.</param>
public sealed class ImportUploadValidationException(string message) : Exception(message);
