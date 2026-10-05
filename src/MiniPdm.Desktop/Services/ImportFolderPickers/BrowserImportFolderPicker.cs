using System.Security.Cryptography;
using System.Text;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MiniPdm.Desktop.Services.ImportFolderPickers;

/// <summary>
/// Принимает выбранные в браузере CAD-файлы через локальный HTTP-мост, когда системный диалог
/// недоступен для удалённой рабочей среды.
/// </summary>
public sealed class BrowserImportFolderPicker : IAsyncDisposable
{
    /// <summary>
    /// Имя HTTP-заголовка с одноразовым токеном авторизации загрузки.
    /// </summary>
    public const string TokenHeader = "X-Pdm-Picker-Token";

    /// <summary>
    /// Максимальный размер тела одного HTTP-запроса в байтах.
    /// </summary>
    public const long MaxRequestBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Максимальный размер одного файла в байтах.
    /// </summary>
    public const long MaxFileBytes = 8L * 1024 * 1024;

    /// <summary>
    /// Максимальное число файлов в одном пакете.
    /// </summary>
    public const int MaxFiles = 1000;
    private static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(5);
    private readonly object _sync = new();
    private readonly int _port;
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"mini-pdm-picker-{Environment.ProcessId}-{Guid.NewGuid():N}");
    private WebApplication? _application;
    private PendingRequest? _pending;
    private bool _disposed;
    private int _activeUploads;
    private TaskCompletionSource _uploadsIdle = CompletedSignal();

    /// <summary>
    /// Создаёт мост и задаёт порт локального HTTP-сервера.
    /// </summary>
    /// <param name="port">Свободный TCP-порт в диапазоне от 1 до 65535.</param>
    public BrowserImportFolderPicker(int port = 6070) => _port = port is > 0 and <= 65535
        ? port : throw new ArgumentOutOfRangeException(nameof(port));

    /// <summary>
    /// Возвращает порт, на котором работает локальный мост.
    /// </summary>
    public int Port => _port;

    /// <summary>
    /// Запускает локальный HTTP-сервер для запросов браузера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены запуска.</param>
    /// <returns>Асинхронная операция запуска сервера.</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_application is not null)
            return;
        Directory.CreateDirectory(_tempRoot);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(_port, listen => listen.Protocols = HttpProtocols.Http1);
            options.Limits.MaxRequestBodySize = MaxRequestBytes;
        });
        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = MaxRequestBytes;
            options.ValueCountLimit = MaxFiles + 1;
            options.MultipartHeadersCountLimit = 8;
            options.MultipartHeadersLengthLimit = 8192;
        });
        var app = builder.Build();
        app.MapGet("/pdm-picker/pending", (Delegate)GetPendingAsync);
        app.MapPost("/pdm-picker/{requestId:guid}/files", UploadAsync);
        app.MapPost("/pdm-picker/{requestId:guid}/cancel", CancelAsync);
        await app.StartAsync(cancellationToken);
        _application = app;
    }

    /// <summary>
    /// Ожидает выбор файлов в браузере и возвращает временно сохранённый пакет.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены ожидания выбора.</param>
    /// <returns>Пакет файлов либо <see langword="null"/>, если выбор отменён браузером.</returns>
    public async Task<SelectedImportPackage?> PickAsync(CancellationToken cancellationToken = default)
    {
        PendingRequest request;
        while (true)
        {
            Task idle;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_application is null)
                    throw new InvalidOperationException("Browser picker bridge is not running.");
                if (_pending is not null)
                    throw new InvalidOperationException("A browser folder selection is already pending.");
                if (_activeUploads == 0)
                {
                    request = new PendingRequest();
                    _pending = request;
                    break;
                }
                idle = _uploadsIdle.Task;
            }
            await idle.WaitAsync(cancellationToken);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestLifetime);
        using var registration = timeout.Token.Register(() => CancelPending(request, timeout.Token));
        try
        {
            return await request.Completion.Task;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            ClearPending(request);
            throw new TimeoutException("Выбор папки в браузере не завершён за 5 минут.");
        }
        finally
        {
            ClearPending(request);
        }
    }

    private Task<IResult> GetPendingAsync(HttpContext context)
    {
        if (!IsLocalSameOrigin(context))
            return Task.FromResult<IResult>(Results.StatusCode(StatusCodes.Status403Forbidden));
        PendingRequest? request;
        lock (_sync)
            request = _pending;
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        if (request is null || request.Completion.Task.IsCompleted)
            return Task.FromResult<IResult>(Results.NoContent());
        return Task.FromResult<IResult>(Results.Json(new
        {
            requestId = request.Id,
            nonce = request.Nonce,
            expiresAt = request.ExpiresAt
        }));
    }

    private async Task<IResult> UploadAsync(HttpContext context, Guid requestId)
    {
        var request = GetAuthorizedRequest(context, requestId);
        if (request is null || !IsLocalSameOrigin(context))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!context.Request.HasFormContentType)
            return Results.BadRequest(new
            {
                error = "Expected multipart/form-data."
            });
        lock (_sync)
        {
            if (!ReferenceEquals(_pending, request) || !request.TryBeginUpload())
                return Results.Conflict(new
                {
                    error = "An upload for this selection is already running or complete."
                });
            if (_activeUploads++ == 0)
                _uploadsIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var packageDirectory = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(packageDirectory);
        var published = false;
        using var uploadCancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, request.CancellationToken);
        var uploadToken = uploadCancellation.Token;
        try
        {
            var form = await context.Request.ReadFormAsync(uploadToken);
            if (form.Files.Count is < 1 or > MaxFiles)
                return Results.BadRequest(new
                {
                    error = $"Select between 1 and {MaxFiles} files."
                });

            var names = new HashSet<string>(StringComparer.Ordinal);
            var paths = new List<string>(form.Files.Count);
            long totalBytes = 0;
            foreach (var file in form.Files)
            {
                var name = file.FileName;
                if (!IsFlatFileName(name) || !Supported(name) || !names.Add(name))
                    return Results.BadRequest(new
                    {
                        error = "File names must be unique flat .a3d/.m3d names without control characters."
                    });
                if (file.Length > MaxFileBytes)
                    return Results.BadRequest(new
                    {
                        error = $"File '{name}' exceeds the 8 MiB per-file limit."
                    });

                var path = Path.Combine(packageDirectory, name);
                await using var source = file.OpenReadStream();
                await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                var buffer = new byte[81920];
                long fileBytes = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, uploadToken);
                    if (read == 0)
                        break;
                    fileBytes += read;
                    totalBytes += read;
                    if (fileBytes > MaxFileBytes || totalBytes > MaxRequestBytes)
                        return Results.BadRequest(new
                        {
                            error = "Uploaded file data exceeded the configured size limit."
                        });
                    await destination.WriteAsync(buffer.AsMemory(0, read), uploadToken);
                }
                if (fileBytes != file.Length)
                    return Results.BadRequest(new
                    {
                        error = $"File '{name}' was incomplete."
                    });
                paths.Add(path);
            }

            lock (_sync)
            {
                if (!ReferenceEquals(_pending, request) || !request.TryCompleteUpload())
                    return Results.Conflict(new
                    {
                        error = "The folder selection has expired or was canceled."
                    });

                var package = new SelectedImportPackage(paths.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray(),
                    new TempDirectoryLease(packageDirectory));
                if (!request.Completion.TrySetResult(package))
                {
                    package.Dispose();
                    return Results.Conflict(new
                    {
                        error = "The folder selection has expired or was canceled."
                    });
                }
                _pending = null;
                published = true;
            }
            return Results.Ok(new
            {
                uploaded = paths.Count
            });
        }
        catch (OperationCanceledException) when (uploadToken.IsCancellationRequested)
        {
            return context.RequestAborted.IsCancellationRequested
                ? Results.StatusCode(499)
                : Results.Conflict(new
                {
                    error = "The folder selection was canceled or expired."
                });
        }
        catch (InvalidDataException ex)
        {
            return Results.BadRequest(new
            {
                error = ex.Message
            });
        }
        finally
        {
            request.EndUpload();
            if (!published)
                TryDeleteDirectory(packageDirectory);
            lock (_sync)
            {
                if (--_activeUploads == 0)
                    _uploadsIdle.TrySetResult();
            }
        }
    }

    private IResult CancelAsync(HttpContext context, Guid requestId)
    {
        if (!IsLocalSameOrigin(context))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        var request = GetAuthorizedRequest(context, requestId);
        if (request is null)
            return Results.NotFound();
        lock (_sync)
        {
            if (!ReferenceEquals(_pending, request))
                return Results.NotFound();
            _pending = null;
        }
        request.CancelUpload();
        request.Completion.TrySetResult(null);
        return Results.NoContent();
    }

    private PendingRequest? GetAuthorizedRequest(HttpContext context, Guid requestId)
    {
        lock (_sync)
        {
            var request = _pending;
            var supplied = context.Request.Headers[TokenHeader].ToString();
            if (request is null || request.Id != requestId || string.IsNullOrEmpty(supplied)
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(request.Nonce), Encoding.UTF8.GetBytes(supplied)))
                return null;
            return request;
        }
    }

    private static bool IsLocalSameOrigin(HttpContext context)
    {
        var host = context.Request.Host.Host.Trim('[', ']');
        if (!IsLoopbackName(host))
            return false;
        if (!context.Request.Headers.TryGetValue("Origin", out var originHeader) || string.IsNullOrWhiteSpace(originHeader))
            return true;
        if (!Uri.TryCreate(originHeader.ToString(), UriKind.Absolute, out var origin))
            return false;
        var hostPort = context.Request.Host.Port ?? (context.Request.IsHttps ? 443 : 80);
        return string.Equals(origin.Host.Trim('[', ']'), host, StringComparison.OrdinalIgnoreCase)
            && origin.Port == hostPort
            && string.Equals(origin.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoopbackName(string host) => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);

    private static bool IsFlatFileName(string name) => !string.IsNullOrWhiteSpace(name)
        && name is not "." and not ".."
        && !name.Contains('/') && !name.Contains('\\')
        && !name.Any(char.IsControl)
        && string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal);

    private static bool Supported(string name) => Path.GetExtension(name).Equals(".a3d", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(name).Equals(".m3d", StringComparison.OrdinalIgnoreCase);

    private void ClearPending(PendingRequest request)
    {
        CancelPending(request, new CancellationToken(canceled: true));
    }

    private void CancelPending(PendingRequest request, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_pending, request))
                return;
            _pending = null;
            request.CancelUpload();
            request.Completion.TrySetCanceled(cancellationToken);
        }
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
    /// Останавливает мост, отменяет незавершённый выбор и удаляет временные данные.
    /// </summary>
    /// <returns>Асинхронная операция освобождения ресурсов.</returns>
    public async ValueTask DisposeAsync()
    {
        PendingRequest? pending;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            pending = _pending;
            _pending = null;
            pending?.CancelUpload();
            pending?.Completion.TrySetCanceled();
        }
        if (_application is not null)
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _application.StopAsync(stopTimeout.Token);
            await _application.DisposeAsync();
        }
        await _uploadsIdle.Task;
        TryDeleteEmptyDirectory(_tempRoot);
    }

    private static TaskCompletionSource CompletedSignal()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completed.SetResult();
        return completed;
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class PendingRequest
    {
        private int _uploading;
        private int _completed;
        private readonly CancellationTokenSource _uploadCancellation = new();

        /// <summary>
        /// Создаёт запрос выбора с уникальным идентификатором, токеном и сроком действия.
        /// </summary>
        public PendingRequest()
        {
            Id = Guid.NewGuid();
            Nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            ExpiresAt = DateTimeOffset.UtcNow.Add(RequestLifetime);
        }

        /// <summary>
        /// Возвращает идентификатор ожидающего выбора.
        /// </summary>
        public Guid Id
        {
            get;
        }

        /// <summary>
        /// Возвращает одноразовый токен для авторизации загрузки.
        /// </summary>
        public string Nonce
        {
            get;
        }

        /// <summary>
        /// Возвращает срок окончания выбора в формате UTC.
        /// </summary>
        public DateTimeOffset ExpiresAt
        {
            get;
        }

        /// <summary>
        /// Возвращает токен отмены текущей загрузки.
        /// </summary>
        public CancellationToken CancellationToken => _uploadCancellation.Token;

        /// <summary>
        /// Возвращает задачу завершения выбора и передачи пакета вызывающему коду.
        /// </summary>
        public TaskCompletionSource<SelectedImportPackage?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Атомарно начинает загрузку, если запрос ещё не был завершён или занят загрузкой.
        /// </summary>
        /// <returns><see langword="true"/>, если загрузка начата.</returns>
        public bool TryBeginUpload() => Volatile.Read(ref _completed) == 0 && Interlocked.CompareExchange(ref _uploading, 1, 0) == 0;

        /// <summary>
        /// Отмечает завершение текущей попытки загрузки.
        /// </summary>
        public void EndUpload() => Volatile.Write(ref _uploading, 0);

        /// <summary>
        /// Атомарно помечает запрос как завершённый.
        /// </summary>
        /// <returns><see langword="true"/>, если именно этот вызов завершил запрос.</returns>
        public bool TryCompleteUpload() => Interlocked.CompareExchange(ref _completed, 1, 0) == 0;

        /// <summary>
        /// Отменяет текущую загрузку, если она выполняется.
        /// </summary>
        public void CancelUpload()
        {
            try
            {
                _uploadCancellation.Cancel();
            }
            catch (ObjectDisposedException) { }
        }
    }

    private sealed class TempDirectoryLease(string path) : IDisposable
    {
        private string? _path = path;

        /// <summary>
        /// Удаляет принадлежащую пакету временную папку; повторные вызовы безопасны.
        /// </summary>
        public void Dispose()
        {
            var owned = Interlocked.Exchange(ref _path, null);
            if (owned is not null)
                TryDeleteDirectory(owned);
        }
    }
}
