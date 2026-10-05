using Resources = MiniPdm.Common.Resources;
using System.Text.Json;
using MiniPdm.Domain.Objects;
using MiniPdm.Modules.Import.Abstractions.Cad;
using MiniPdm.Modules.Import.DtoModels.Cad;

namespace MiniPdm.Modules.Import.Infrastructure.Cad;

/// <summary>
/// Читает JSON-описания CAD-документов из файлового каталога.
/// </summary>
/// <param name="directory">Каталог, содержащий файлы пакета.</param>
public sealed class FileJsonCadDocumentReader(string directory) : ICadDocumentReader
{
    private static readonly JsonDocumentOptions JsonOptions = new() { CommentHandling = JsonCommentHandling.Disallow };

    /// <inheritdoc />
    public async Task<CadReadResultDto> ReadAsync(CadDocumentRefDto document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsFileName(document.FileName))
            return Failure(Resources.InputLogicException.CadReferenceFilename);

        var path = Path.Combine(directory, document.FileName);
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            return Failure(string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.InputLogicException.CadNotFound, document.FileName));
        }
        catch (DirectoryNotFoundException)
        {
            return Failure(string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.InputLogicException.CadNotFound, document.FileName));
        }
        catch (IOException ex)
        {
            return Failure(string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.InputLogicException.CadReadError, document.FileName, ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Failure(string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.InputLogicException.CadReadError, document.FileName, ex.Message));
        }

        try
        {
            var jsonBytes = bytes.AsMemory(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0);
            using var json = JsonDocument.Parse(jsonBytes, JsonOptions);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Failure(Resources.InputLogicException.CadJsonObject);

            if (!root.TryGetProperty("formatVersion", out var formatVersion) ||
                formatVersion.ValueKind != JsonValueKind.Number || !formatVersion.TryGetInt32(out var version) || version != 1)
                return Failure(Resources.InputLogicException.CadFormatVersion);

            if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
                return Failure(Resources.InputLogicException.CadTypeMissing);
            var typeText = typeElement.GetString();
            var type = typeText switch
            {
                "Assembly" => PdmObjectType.Assembly,
                "Part" => PdmObjectType.Part,
                "StandardPart" => PdmObjectType.StandardPart,
                _ => (PdmObjectType?)null
            };
            if (type is null)
                return Failure(Resources.InputLogicException.CadTypeInvalid);

            if (!root.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
                return Failure(Resources.InputLogicException.CadNameMissing);

            string? designation = ReadOptionalString(root, "designation", out var designationError);
            if (designationError is not null)
                return Failure(designationError);

            var name = nameElement.GetString()!;
            CadDocumentDto Partial(string? partialMaterial = null, decimal? partialMass = null,
                IReadOnlyList<CadComponentDto>? partialComponents = null) =>
                new()
                {
                    FileName = document.FileName,
                    Type = type.Value,
                    Designation = designation,
                    Name = name,
                    Material = partialMaterial,
                    Mass = partialMass,
                    Components = partialComponents ?? Array.Empty<CadComponentDto>()
                };

            var extension = Path.GetExtension(document.FileName);
            if ((type == PdmObjectType.Assembly && !extension.Equals(".a3d", StringComparison.OrdinalIgnoreCase)) ||
                (type != PdmObjectType.Assembly && !extension.Equals(".m3d", StringComparison.OrdinalIgnoreCase)))
                return Failure(string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.InputLogicException.CadExtensionMismatch, extension, typeText), Partial());

            string? material = null;
            decimal? mass = null;
            if (root.TryGetProperty("properties", out var properties) && properties.ValueKind != JsonValueKind.Null)
            {
                if (properties.ValueKind != JsonValueKind.Object)
                    return Failure(Resources.InputLogicException.CadPropertiesObject, Partial());
                material = ReadOptionalString(properties, "material", out var materialError);
                if (materialError is not null)
                    return Failure(materialError, Partial());
                if (properties.TryGetProperty("mass", out var massElement) && massElement.ValueKind != JsonValueKind.Null)
                {
                    if (massElement.ValueKind != JsonValueKind.Number || !massElement.TryGetDecimal(out var parsedMass))
                        return Failure(Resources.InputLogicException.CadMassInvalid, Partial(material));
                    mass = parsedMass;
                }
            }

            if (!root.TryGetProperty("components", out var componentsElement) || componentsElement.ValueKind != JsonValueKind.Array)
                return Failure(Resources.InputLogicException.CadComponentsArray, Partial(material, mass));

            var components = new List<CadComponentDto> { };
            foreach (var component in componentsElement.EnumerateArray())
            {
                if (component.ValueKind != JsonValueKind.Object ||
                    !component.TryGetProperty("file", out var fileElement) || fileElement.ValueKind != JsonValueKind.String ||
                    !IsFileName(fileElement.GetString()) ||
                    !component.TryGetProperty("count", out var countElement) || countElement.ValueKind != JsonValueKind.Number ||
                    !countElement.TryGetInt32(out var count))
                    return Failure(Resources.InputLogicException.CadComponentInvalid, Partial(material, mass, components));
                components.Add(new CadComponentDto
                {
                    File = fileElement.GetString()!,
                    Count = count
                });
            }

            return new CadReadResultDto
            {
                Document = Partial(material, mass, components),
                Error = null
            };
        }
        catch (JsonException ex)
        {
            return Failure(string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.InputLogicException.CadJsonInvalid, document.FileName, ex.Message));
        }
    }

    private static string? ReadOptionalString(JsonElement parent, string property, out string? error)
    {
        error = null;
        if (!parent.TryGetProperty(property, out var element) || element.ValueKind == JsonValueKind.Null)
            return null;
        if (element.ValueKind != JsonValueKind.String)
        {
            error = string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.InputLogicException.CadPropertyInvalid, property);
            return null;
        }
        return element.GetString();
    }

    private static bool IsFileName(string? value) => !string.IsNullOrWhiteSpace(value) &&
        string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal) &&
        !value.Contains('/') && !value.Contains('\\') && value is not "." and not "..";

    private static CadReadResultDto Failure(string error, CadDocumentDto? partialDocument = null) => new()
    {
        Document = partialDocument,
        Error = error
    };
}
