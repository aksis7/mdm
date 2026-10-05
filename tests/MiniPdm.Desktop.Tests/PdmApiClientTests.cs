using System.Net;
using System.Text;
using MiniPdm.Contracts.Modules.Import.DtoModels;
using MiniPdm.Desktop.Services;
using Xunit;

namespace MiniPdm.Desktop.Tests;

/// <summary>
/// Проверяет формирование HTTP-запросов и обработку ответов клиентом API Mini-PDM.
/// </summary>
public sealed class PdmApiClientTests
{
    /// <summary>
    /// Проверяет отправку файлов с Unicode-именами и числовыми значениями перечислений, а также повторное получение отчёта по тому же идентификатору.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [Fact]
    public async Task ImportUsesMultipartUnicodeFileNamesAndNumericEnumsAndCanReplayReportBySameId()
    {
        var importId = Guid.NewGuid();
        var filePath = Path.Combine(Path.GetTempPath(), $"деталь-{Guid.NewGuid():N}.m3d");
        await File.WriteAllTextAsync(filePath, "{}", Encoding.UTF8);
        try
        {
            var requestCount = 0;
            var handler = new RecordingHandler(async request =>
            {
                Assert.Equal($"api/imports/{importId:D}", request.RequestUri!.PathAndQuery.TrimStart('/'));
                if (request.Method == HttpMethod.Post)
                {
                    requestCount++;
                    Assert.Equal("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);
                    var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
                    var filePart = Assert.Single(multipart);
                    var disposition = filePart.Headers.ContentDisposition!;
                    Assert.Contains("деталь", $"{disposition.FileName} {disposition.FileNameStar}", StringComparison.Ordinal);
                    var body = await request.Content.ReadAsStringAsync();
                    Assert.Contains("filename", body, StringComparison.OrdinalIgnoreCase);
                    return Json(HttpStatusCode.OK,
                        $$"""{"importId":"{{importId:D}}","files":[{"fileName":"деталь.m3d","status":0,"reason":null,"action":0,"warnings":[] }]}""");
                }

                Assert.Equal(HttpMethod.Get, request.Method);
                return Json(HttpStatusCode.OK,
                    $$"""{"importId":"{{importId:D}}","files":[{"fileName":"деталь.m3d","status":0,"reason":null,"action":0,"warnings":[] }]}""");
            });
            using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
            var client = new PdmApiClient(httpClient);

            var uploaded = await client.ImportFilesAsync(importId, [filePath]);
            var replayed = await client.GetImportReportAsync(importId);

            Assert.Equal(importId, uploaded.ImportId);
            Assert.Equal(ImportFileStatus.Accepted, Assert.Single(uploaded.Files).Status);
            Assert.Equal<ImportFileAction?>(ImportFileAction.Created, Assert.Single(uploaded.Files).Action);
            Assert.Equal(importId, replayed.ImportId);
            Assert.Equal(1, requestCount);
            Assert.Equal(new[] { HttpMethod.Post, HttpMethod.Get }, handler.Requests.Select(x => x.Method));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Проверяет сохранение сведений о конфликте и цикле, а также распознавание неопределённого результата ответа 503.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [Fact]
    public async Task ApiExceptionPreservesConflictCycleAndMarksUnknown503Outcome()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var handler = new RecordingHandler(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/composition", StringComparison.Ordinal)
            ? Json(HttpStatusCode.Conflict, $$"""{"code":"Cycle","message":"The composition creates a cycle.","cyclePath":["{{a:D}}","{{b:D}}","{{a:D}}"]}""")
            : Json(HttpStatusCode.ServiceUnavailable, "{\"title\":\"The version write outcome is unknown.\"}")));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        var client = new PdmApiClient(httpClient);

        var conflict = await Assert.ThrowsAsync<PdmApiException>(() => client.GetCompositionAsync(Guid.NewGuid()));
        var uncertain = await Assert.ThrowsAsync<PdmApiException>(() => client.GetObjectAsync(Guid.NewGuid()));

        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal("Cycle", conflict.ErrorCode);
        Assert.Equal(new[] { a, b, a }, conflict.CyclePath);
        Assert.Contains(a.ToString(), conflict.Message);
        Assert.Equal(503, uncertain.StatusCode);
        Assert.True(uncertain.IsOutcomeUnknown);
    }

    /// <summary>
    /// Проверяет проверку HTTP-схемы базового адреса и сохранение заданного пути.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [Fact]
    public async Task BaseAddressValidatesHttpSchemeAndRetainsConfiguredPath()
    {
        var previous = Environment.GetEnvironmentVariable("PDM_API_BASE_URL");
        try
        {
            Environment.SetEnvironmentVariable("PDM_API_BASE_URL", "ftp://example.test/pdm");
            Assert.Throws<InvalidOperationException>(() => PdmApiClientOptions.FromEnvironment());

            Environment.SetEnvironmentVariable("PDM_API_BASE_URL", "https://example.test/pdm");
            var options = PdmApiClientOptions.FromEnvironment();
            var handler = new RecordingHandler(_ => Task.FromResult(Json(HttpStatusCode.OK,
                "{\"items\":[],\"offset\":0,\"limit\":50,\"hasMore\":false}")));
            using var httpClient = new HttpClient(handler) { BaseAddress = options.BaseAddress };
            var client = new PdmApiClient(httpClient);

            await client.SearchObjectsAsync("A B", offset: 2);

            Assert.Equal("/pdm/api/objects", handler.Requests.Single().RequestUri!.AbsolutePath);
            Assert.Contains("search=A%20B", handler.Requests.Single().RequestUri!.Query);
            Assert.EndsWith("/", options.BaseAddress.AbsoluteUri);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PDM_API_BASE_URL", previous);
        }
    }

    /// <summary>
    /// Создаёт HTTP-ответ с JSON-содержимым и указанным статусом.
    /// </summary>
    /// <param name="status">Код состояния HTTP-ответа.</param>
    /// <param name="content">Текст содержимого HTTP-ответа.</param>
    /// <returns>HTTP-ответ с указанным кодом состояния и JSON-содержимым.</returns>
    private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        /// <summary>
        /// HTTP-запросы, полученные тестовым обработчиком.
        /// </summary>
        public List<HttpRequestMessage> Requests { get; } = [];

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return responder(request);
        }
    }
}
