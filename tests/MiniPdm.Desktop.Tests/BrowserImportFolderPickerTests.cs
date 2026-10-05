using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using MiniPdm.Desktop.Services.ImportFolderPickers;
using Xunit;

namespace MiniPdm.Desktop.Tests;

/// <summary>
/// Проверяет HTTP-загрузку файлов через браузерный выбор папки, включая отказ, отмену и освобождение временных файлов.
/// </summary>
public sealed class BrowserImportFolderPickerTests
{
    /// <summary>
    /// Проверяет, что загрузка через браузер возвращает файлы во временной аренде и удаляет их после освобождения аренды.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [Fact]
    public async Task BrowserUploadReturnsLeasedFilesAndDeletesThemWhenLeaseIsReleased()
    {
        await using var bridge = new BrowserImportFolderPicker(ReservePort());
        await bridge.StartAsync();
        using var http = Client(bridge);
        var selection = bridge.PickAsync();
        var pending = await WaitForPendingAsync(http);

        using var form = new MultipartFormDataContent();
        Add(form, "bracket.a3d", [1, 2, 3]);
        Add(form, "profile.M3D", [4, 5]);
        using var response = await UploadAsync(http, pending, form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var package = (await selection.WaitAsync(TimeSpan.FromSeconds(3)))!;
        Assert.Equal(2, package.FilePaths.Count);
        Assert.All(package.FilePaths, path => Assert.True(File.Exists(path)));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(package.FilePaths.Single(x => x.EndsWith("bracket.a3d", StringComparison.Ordinal))));
        var paths = package.FilePaths.ToArray();

        package.Dispose();

        Assert.All(paths, path => Assert.False(File.Exists(path)));
    }

    /// <summary>
    /// Проверяет, что некорректная загрузка удаляется, а ожидающий выбор остаётся доступен для корректной загрузки.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [Fact]
    public async Task InvalidUploadIsRemovedAndDoesNotResolvePendingSelection()
    {
        await using var bridge = new BrowserImportFolderPicker(ReservePort());
        await bridge.StartAsync();
        using var http = Client(bridge);
        var selection = bridge.PickAsync();
        var pending = await WaitForPendingAsync(http);

        using (var form = new MultipartFormDataContent())
        {
            Add(form, "../outside.a3d", [1]);
            using var rejected = await UploadAsync(http, pending, form);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        Assert.False(selection.IsCompleted);
        using (var form = new MultipartFormDataContent())
        {
            Add(form, "valid.a3d", [8]);
            using var accepted = await UploadAsync(http, pending, form);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        using var package = (await selection.WaitAsync(TimeSpan.FromSeconds(3)))!;
        Assert.Single(package.FilePaths);
        Assert.Equal(new byte[] { 8 }, await File.ReadAllBytesAsync(package.FilePaths[0]));
    }

    /// <summary>
    /// Проверяет защиту запросов токеном и источником, а также завершение ожидания после отмены выбора.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [Fact]
    public async Task PickerTokenAndSameHostOriginAreRequiredAndCancelCompletesWaiter()
    {
        await using var bridge = new BrowserImportFolderPicker(ReservePort());
        await bridge.StartAsync();
        using var http = Client(bridge);
        var selection = bridge.PickAsync();
        var pending = await WaitForPendingAsync(http);

        using (var form = new MultipartFormDataContent())
        using (var response = await http.PostAsync($"/pdm-picker/{pending.RequestId}/files", form))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using (var badOrigin = new HttpRequestMessage(HttpMethod.Post, $"/pdm-picker/{pending.RequestId}/cancel"))
        {
            badOrigin.Headers.TryAddWithoutValidation(BrowserImportFolderPicker.TokenHeader, pending.Nonce);
            badOrigin.Headers.TryAddWithoutValidation("Origin", $"http://127.0.0.1:{bridge.Port + 1}");
            using var response = await http.SendAsync(badOrigin);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using (var cancel = new HttpRequestMessage(HttpMethod.Post, $"/pdm-picker/{pending.RequestId}/cancel"))
        {
            cancel.Headers.TryAddWithoutValidation(BrowserImportFolderPicker.TokenHeader, pending.Nonce);
            using var response = await http.SendAsync(cancel);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Null(await selection.WaitAsync(TimeSpan.FromSeconds(3)));
        using var staleForm = new MultipartFormDataContent();
        Add(staleForm, "stale.a3d", [1]);
        using var staleResponse = await UploadAsync(http, pending, staleForm);
        Assert.Equal(HttpStatusCode.Forbidden, staleResponse.StatusCode);
    }

    /// <summary>
    /// Проверяет отклонение повторяющихся и слишком больших файлов без завершения ожидающего запроса.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [Fact]
    public async Task DuplicateAndOversizedFilesAreRejectedWithoutConsumingThePendingRequest()
    {
        await using var bridge = new BrowserImportFolderPicker(ReservePort());
        await bridge.StartAsync();
        using var http = Client(bridge);
        var selection = bridge.PickAsync();
        var pending = await WaitForPendingAsync(http);

        using (var form = new MultipartFormDataContent())
        {
            Add(form, "same.a3d", [1]);
            Add(form, "same.a3d", [2]);
            using var duplicate = await UploadAsync(http, pending, form);
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        }
        using (var form = new MultipartFormDataContent())
        {
            Add(form, "large.m3d", new byte[BrowserImportFolderPicker.MaxFileBytes + 1]);
            using var oversized = await UploadAsync(http, pending, form);
            Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        }

        Assert.False(selection.IsCompleted);
        using (var form = new MultipartFormDataContent())
        {
            Add(form, "accepted.a3d", [3]);
            using var accepted = await UploadAsync(http, pending, form);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }
        using var package = (await selection.WaitAsync(TimeSpan.FromSeconds(3)))!;
        Assert.Single(package.FilePaths);
    }

    /// <summary>
    /// Проверяет, что отмена во время загрузки прекращает чтение и отбрасывает неполный пакет.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [Fact]
    public async Task CancelDuringUploadAbortsTheReadAndDiscardsPartialPacket()
    {
        await using var bridge = new BrowserImportFolderPicker(ReservePort());
        await bridge.StartAsync();
        using var http = Client(bridge);
        var selection = bridge.PickAsync();
        var pending = await WaitForPendingAsync(http);
        var content = new PausedMultipartContent();
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, $"/pdm-picker/{pending.RequestId}/files") { Content = content };
        uploadRequest.Headers.TryAddWithoutValidation(BrowserImportFolderPicker.TokenHeader, pending.Nonce);
        var upload = http.SendAsync(uploadRequest);
        await content.FirstChunkWritten.Task.WaitAsync(TimeSpan.FromSeconds(3));

        using var cancel = new HttpRequestMessage(HttpMethod.Post, $"/pdm-picker/{pending.RequestId}/cancel");
        cancel.Headers.TryAddWithoutValidation(BrowserImportFolderPicker.TokenHeader, pending.Nonce);
        using var canceled = await http.SendAsync(cancel);
        Assert.Equal(HttpStatusCode.NoContent, canceled.StatusCode);
        Assert.Null(await selection.WaitAsync(TimeSpan.FromSeconds(3)));

        content.Release.TrySetResult();
        try
        {
            using var ignored = await upload.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException) { }
    }

    /// <summary>
    /// Создаёт HTTP-клиент для локального сервера выбора файлов.
    /// </summary>
    /// <param name="bridge">Сервер выбора файлов, к которому подключается клиент.</param>
    /// <returns>HTTP-клиент с базовым адресом запущенного локального сервера выбора файлов.</returns>
    private static HttpClient Client(BrowserImportFolderPicker bridge) =>
        new()
        {
            BaseAddress = new Uri($"http://127.0.0.1:{bridge.Port}/")
        };

    /// <summary>
    /// Ожидает появления ожидающего запроса выбора файлов и возвращает его сведения.
    /// </summary>
    /// <param name="http">HTTP-клиент для обращения к тестовому серверу.</param>
    /// <returns>Сведения о запросе выбора файлов, появившемся на локальном сервере до истечения срока ожидания.</returns>
    private static async Task<PendingDto> WaitForPendingAsync(HttpClient http)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            using var response = await http.GetAsync("/pdm-picker/pending");
            if (response.StatusCode == HttpStatusCode.OK)
                return (await response.Content.ReadFromJsonAsync<PendingDto>())!;
            await Task.Delay(20);
        }
        throw new TimeoutException("Picker request did not appear.");
    }

    /// <summary>
    /// Отправляет выбранные файлы для ожидающего запроса и возвращает HTTP-ответ.
    /// </summary>
    /// <param name="http">HTTP-клиент для обращения к тестовому серверу.</param>
    /// <param name="pending">Сведения об ожидающем запросе выбора файлов.</param>
    /// <param name="form">Multipart-форма с загружаемыми файлами.</param>
    /// <returns>HTTP-ответ сервера на загрузку формы для ожидающего запроса выбора файлов.</returns>
    private static async Task<HttpResponseMessage> UploadAsync(HttpClient http, PendingDto pending, MultipartFormDataContent form)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/pdm-picker/{pending.RequestId}/files") { Content = form };
        request.Headers.TryAddWithoutValidation(BrowserImportFolderPicker.TokenHeader, pending.Nonce);
        return await http.SendAsync(request);
    }

    /// <summary>
    /// Добавляет содержимое файла в multipart-запрос.
    /// </summary>
    /// <param name="form">Multipart-форма с загружаемыми файлами.</param>
    /// <param name="fileName">Имя файла, добавляемого в форму.</param>
    /// <param name="bytes">Байты содержимого файла.</param>
    private static void Add(MultipartFormDataContent form, string fileName, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "files", fileName);
    }

    private sealed class PausedMultipartContent : HttpContent
    {
        private const string Boundary = "pdm-test-boundary";
        /// <summary>
        /// Создаёт потоковое multipart-содержимое, которое позволяет проверить отмену незавершённой загрузки.
        /// </summary>
        public PausedMultipartContent()
        {
            Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data");
            Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", Boundary));
        }
        /// <summary>
        /// Сигнализирует, что первая часть содержимого отправлена серверу.
        /// </summary>
        public TaskCompletionSource FirstChunkWritten { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>
        /// Разрешает продолжение передачи отложенной части содержимого.
        /// </summary>
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc/>
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var header = Encoding.ASCII.GetBytes($"--{Boundary}\r\nContent-Disposition: form-data; name=\"files\"; filename=\"slow.a3d\"\r\nContent-Type: application/octet-stream\r\n\r\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(new byte[4096]);
            FirstChunkWritten.TrySetResult();
            await Release.Task;
            await stream.WriteAsync(new byte[4096]);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"\r\n--{Boundary}--\r\n"));
        }

        /// <inheritdoc/>
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        /// <inheritdoc/>
        protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        /// <inheritdoc/>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            SerializeToStreamAsync(stream, context);
    }

    /// <summary>
    /// Резервирует свободный локальный TCP-порт для тестового сервера.
    /// </summary>
    /// <returns>Номер свободного TCP-порта на loopback-интерфейсе.</returns>
    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>
    /// Представляет ожидающий запрос выбора папки импорта в тестовом обработчике.
    /// </summary>
    private sealed record PendingDto(Guid RequestId, string Nonce, DateTimeOffset ExpiresAt)
    {
        /// <summary>
        /// Идентификатор запроса на выбор папки.
        /// </summary>
        public Guid RequestId { get; init; } = RequestId;

        /// <summary>
        /// Секретный маркер, связывающий запрос с его ответом.
        /// </summary>
        public string Nonce { get; init; } = Nonce;

        /// <summary>
        /// Момент истечения срока действия запроса.
        /// </summary>
        public DateTimeOffset ExpiresAt { get; init; } = ExpiresAt;
    }
}
