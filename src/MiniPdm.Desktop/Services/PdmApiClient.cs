using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MiniPdm.Contracts.Modules.BackgroundTasks.DtoModels;
using MiniPdm.Contracts.Modules.Calculations.DtoModels;
using MiniPdm.Contracts.Modules.Composition.DtoModels;
using MiniPdm.Contracts.Modules.Import.DtoModels;
using MiniPdm.Contracts.Modules.Objects.DtoModels;
using MiniPdm.Contracts.Modules.Versions.DtoModels;
using MiniPdm.Desktop.Services.Abstractions;

namespace MiniPdm.Desktop.Services;

/// <summary>
/// Реализация клиента HTTP для API системы PDM.
/// </summary>
/// <param name="httpClient">HTTP-клиент с настроенным базовым адресом сервера.</param>
public sealed class PdmApiClient(HttpClient httpClient) : IPdmApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public Task<ObjectSearchPageDto> SearchObjectsAsync(string? search = null, int offset = 0, int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var query = $"?search={Uri.EscapeDataString(search ?? string.Empty)}&offset={offset}&limit={limit}";
        return GetAsync<ObjectSearchPageDto>($"api/objects{query}", cancellationToken);
    }

    /// <inheritdoc />
    public Task<ObjectCardDto> GetObjectAsync(Guid objectId, int? version = null,
        CancellationToken cancellationToken = default)
    {
        var path = $"api/objects/{objectId:D}";
        if (version is not null)
            path += $"?version={version.Value}";
        return GetAsync<ObjectCardDto>(path, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CompositionTreeDto> GetCompositionAsync(Guid objectId, CancellationToken cancellationToken = default) =>
        GetAsync<CompositionTreeDto>($"api/objects/{objectId:D}/composition", cancellationToken);

    /// <inheritdoc />
    public Task<VersionCompositionDto> GetVersionCompositionAsync(Guid objectId, int version,
        CancellationToken cancellationToken = default) =>
        GetAsync<VersionCompositionDto>($"api/objects/{objectId:D}/versions/{version}/composition", cancellationToken);

    /// <inheritdoc />
    public Task<CompositionCalculationDto> GetCalculationAsync(Guid objectId, CancellationToken cancellationToken = default) =>
        GetAsync<CompositionCalculationDto>($"api/objects/{objectId:D}/calculations", cancellationToken);

    /// <inheritdoc />
    public async Task<ImportReportDto> ImportFilesAsync(Guid importId, IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        using var form = new MultipartFormDataContent();
        foreach (var filePath in filePaths)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A file path cannot be empty.", nameof(filePaths));
            var fileContent = new StreamContent(File.OpenRead(filePath));
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "files", Path.GetFileName(filePath));
        }

        using var response = await httpClient.PostAsync($"api/imports/{importId:D}", form, cancellationToken);
        return await ReadResponseAsync<ImportReportDto>(response, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ImportReportDto> GetImportReportAsync(Guid importId, CancellationToken cancellationToken = default) =>
        GetAsync<ImportReportDto>($"api/imports/{importId:D}", cancellationToken);

    /// <inheritdoc />
    public Task<VersionMutationDto> CloneVersionAsync(Guid objectId, CloneVersionRequestDto request,
        CancellationToken cancellationToken = default) =>
        PostJsonAsync<CloneVersionRequestDto, VersionMutationDto>($"api/objects/{objectId:D}/versions", request, cancellationToken);

    /// <inheritdoc />
    public Task<VersionMutationDto> ChangeVersionStateAsync(Guid objectId, int version,
        ChangeVersionStateRequestDto request, CancellationToken cancellationToken = default) =>
        PutJsonAsync<ChangeVersionStateRequestDto, VersionMutationDto>(
            $"api/objects/{objectId:D}/versions/{version}/state", request, cancellationToken);

    /// <inheritdoc />
    public Task<VersionMutationDto> UpdateVersionAttributesAsync(Guid objectId, int version,
        UpdateVersionAttributesRequestDto request, CancellationToken cancellationToken = default) =>
        PutJsonAsync<UpdateVersionAttributesRequestDto, VersionMutationDto>(
            $"api/objects/{objectId:D}/versions/{version}/attributes", request, cancellationToken);

    /// <inheritdoc />
    public Task<VersionMutationDto> ReplaceCompositionAsync(Guid objectId, int version,
        ReplaceCompositionRequestDto request, CancellationToken cancellationToken = default) =>
        PutJsonAsync<ReplaceCompositionRequestDto, VersionMutationDto>(
            $"api/objects/{objectId:D}/versions/{version}/composition", request, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<BackgroundTaskDto>> GetBackgroundTasksAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<BackgroundTaskDto>>("api/background-tasks", cancellationToken);

    /// <inheritdoc />
    public Task<BackgroundTaskDto> UpdateBackgroundTaskScheduleAsync(string taskId,
        UpdateBackgroundTaskScheduleRequestDto request, CancellationToken cancellationToken = default) =>
        PutJsonAsync<UpdateBackgroundTaskScheduleRequestDto, BackgroundTaskDto>(
            $"api/background-tasks/{Uri.EscapeDataString(taskId)}/schedule", request, cancellationToken);

    /// <inheritdoc />
    public async Task<BackgroundTaskRunAcceptedDto> RunBackgroundTaskAsync(string taskId,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync(
            $"api/background-tasks/{Uri.EscapeDataString(taskId)}/run", content: null, cancellationToken);
        return await ReadResponseAsync<BackgroundTaskRunAcceptedDto>(response, cancellationToken);
    }

    private async Task<T> GetAsync<T>(string relativeUri, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(relativeUri, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(string relativeUri, TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(relativeUri, request, JsonOptions, cancellationToken);
        return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }

    private async Task<TResponse> PutJsonAsync<TRequest, TResponse>(string relativeUri, TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync(relativeUri, request, JsonOptions, cancellationToken);
        return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw ParseApiException((int)response.StatusCode, body);
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new PdmApiException((int)response.StatusCode,
            "The API returned an empty response where a result was expected.");
    }

    private static PdmApiException ParseApiException(int statusCode, string body)
    {
        string? code = null;
        string? message = null;
        Guid[]? cyclePath = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                var root = document.RootElement;
                code = GetString(root, "code", "errorCode");
                message = GetString(root, "message", "detail", "title", "error");
                if (TryGetProperty(root, "cyclePath", out var pathElement) && pathElement.ValueKind == JsonValueKind.Array)
                    cyclePath = pathElement.EnumerateArray()
                        .Select(x => x.ValueKind == JsonValueKind.String && Guid.TryParse(x.GetString(), out var id)
                            ? (Guid?)id : null)
                        .Where(x => x.HasValue).Select(x => x!.Value).ToArray();
            }
            else if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                message = document.RootElement.GetString();
            }
        }
        catch (JsonException)
        {
            // Keep the body as diagnostic context for non-JSON errors such as proxy failures.
        }

        message ??= string.IsNullOrWhiteSpace(body) ? $"The API returned HTTP {statusCode}." : body.Trim();
        return new PdmApiException(statusCode, message, code, cyclePath, body);
    }

    private static string? GetString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (TryGetProperty(root, name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        return null;
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        value = default;
        return false;
    }
}
