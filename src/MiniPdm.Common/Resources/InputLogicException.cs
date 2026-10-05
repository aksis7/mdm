using System.Globalization;
using System.Resources;

namespace MiniPdm.Common.Resources;

/// <summary>
/// Тексты ошибок некорректных входных данных.
/// </summary>
public static class InputLogicException
{
    private static readonly ResourceManager Manager = new("MiniPdm.Common.Resources.InputLogicException", typeof(InputLogicException).Assembly);
    /// <summary>
    /// Возвращает текст сообщения: Object ID must not be empty.
    /// </summary>
    public static string ObjectIdRequired => Manager.GetString(nameof(ObjectIdRequired), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Version must be positive.
    /// </summary>
    public static string VersionMustBePositive => Manager.GetString(nameof(VersionMustBePositive), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: A request body is required.
    /// </summary>
    public static string RequestBodyRequired => Manager.GetString(nameof(RequestBodyRequired), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Expected concurrency token must not be empty.
    /// </summary>
    public static string ConcurrencyTokenRequired => Manager.GetString(nameof(ConcurrencyTokenRequired), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Search must be at most 512 characters.
    /// </summary>
    public static string SearchTooLong => Manager.GetString(nameof(SearchTooLong), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Offset must be nonnegative.
    /// </summary>
    public static string OffsetNonnegative => Manager.GetString(nameof(OffsetNonnegative), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Limit must be between 1 and 100.
    /// </summary>
    public static string LimitRange => Manager.GetString(nameof(LimitRange), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Components must be provided.
    /// </summary>
    public static string ComponentsRequired => Manager.GetString(nameof(ComponentsRequired), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Each component must have a non-empty child object ID.
    /// </summary>
    public static string ChildObjectIdRequired => Manager.GetString(nameof(ChildObjectIdRequired), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Import ID must not be empty.
    /// </summary>
    public static string ImportIdRequired => Manager.GetString(nameof(ImportIdRequired), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Expected multipart/form-data.
    /// </summary>
    public static string MultipartRequired => Manager.GetString(nameof(MultipartRequired), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Malformed multipart upload.
    /// </summary>
    public static string MalformedMultipart => Manager.GetString(nameof(MalformedMultipart), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: A valid Cyrillic designation is required.
    /// </summary>
    public static string DesignationRequired => Manager.GetString(nameof(DesignationRequired), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Component '{0}' must have a positive count.
    /// </summary>
    public static string ComponentCountPositive => Manager.GetString(nameof(ComponentCountPositive), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Component file '{0}' is missing from this package.
    /// </summary>
    public static string ComponentFileMissing => Manager.GetString(nameof(ComponentFileMissing), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Component file '{0}' was rejected.
    /// </summary>
    public static string ComponentFileRejected => Manager.GetString(nameof(ComponentFileRejected), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Name is required.
    /// </summary>
    public static string PayloadNameRequired => Manager.GetString(nameof(PayloadNameRequired), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Name exceeds 512 characters.
    /// </summary>
    public static string PayloadNameTooLong => Manager.GetString(nameof(PayloadNameTooLong), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Material exceeds 256 characters.
    /// </summary>
    public static string MaterialTooLong => Manager.GetString(nameof(MaterialTooLong), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Normalized standard part name exceeds 512 characters.
    /// </summary>
    public static string StandardNameTooLong => Manager.GetString(nameof(StandardNameTooLong), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Mass cannot be negative.
    /// </summary>
    public static string MassNegative => Manager.GetString(nameof(MassNegative), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Mass must fit decimal(18,6).
    /// </summary>
    public static string MassOutOfRange => Manager.GetString(nameof(MassOutOfRange), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Assembly material and mass must be absent.
    /// </summary>
    public static string AssemblyAttributesInvalid => Manager.GetString(nameof(AssemblyAttributesInvalid), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Part material is required.
    /// </summary>
    public static string PartMaterialRequired => Manager.GetString(nameof(PartMaterialRequired), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Standard parts must not have material.
    /// </summary>
    public static string StandardMaterialInvalid => Manager.GetString(nameof(StandardMaterialInvalid), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Standard part mass is required.
    /// </summary>
    public static string StandardMassRequired => Manager.GetString(nameof(StandardMassRequired), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Only assemblies may contain components.
    /// </summary>
    public static string CompositionMustBeAssembly => Manager.GetString(nameof(CompositionMustBeAssembly), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Source version must be positive.
    /// </summary>
    public static string SourceVersionPositive => Manager.GetString(nameof(SourceVersionPositive), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: State must be InWork, Approved, or Cancelled.
    /// </summary>
    public static string StateInvalid => Manager.GetString(nameof(StateInvalid), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: IntervalMinutes must be between 1 and 525600.
    /// </summary>
    public static string IntervalRange => Manager.GetString(nameof(IntervalRange), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: CAD document must be a JSON object.
    /// </summary>
    public static string CadJsonObject => Manager.GetString(nameof(CadJsonObject), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Unsupported or missing formatVersion; expected 1.
    /// </summary>
    public static string CadFormatVersion => Manager.GetString(nameof(CadFormatVersion), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Missing or invalid type; expected Assembly, Part, or StandardPart.
    /// </summary>
    public static string CadTypeMissing => Manager.GetString(nameof(CadTypeMissing), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Invalid type; expected Assembly, Part, or StandardPart.
    /// </summary>
    public static string CadTypeInvalid => Manager.GetString(nameof(CadTypeInvalid), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Missing or invalid name.
    /// </summary>
    public static string CadNameMissing => Manager.GetString(nameof(CadNameMissing), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: CAD file extension '{0}' does not match type '{1}'.
    /// </summary>
    public static string CadExtensionMismatch => Manager.GetString(nameof(CadExtensionMismatch), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: properties must be an object or null.
    /// </summary>
    public static string CadPropertiesObject => Manager.GetString(nameof(CadPropertiesObject), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: properties.mass must be a decimal number or null.
    /// </summary>
    public static string CadMassInvalid => Manager.GetString(nameof(CadMassInvalid), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: components must be an array.
    /// </summary>
    public static string CadComponentsArray => Manager.GetString(nameof(CadComponentsArray), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Each component must have a file name and an integer count.
    /// </summary>
    public static string CadComponentInvalid => Manager.GetString(nameof(CadComponentInvalid), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Invalid JSON in CAD file '{0}': {1}
    /// </summary>
    public static string CadJsonInvalid => Manager.GetString(nameof(CadJsonInvalid), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: {0} must be a string or null.
    /// </summary>
    public static string CadPropertyInvalid => Manager.GetString(nameof(CadPropertyInvalid), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: CAD file '{0}' was not found.
    /// </summary>
    public static string CadNotFound => Manager.GetString(nameof(CadNotFound), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Could not read CAD file '{0}': {1}
    /// </summary>
    public static string CadReadError => Manager.GetString(nameof(CadReadError), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The field {0} must be between {1} and {2}.
    /// </summary>
    public static string RangeOneTo525600 => Manager.GetString(nameof(RangeOneTo525600), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Document reference must contain a file name only.
    /// </summary>
    public static string CadReferenceFilename => Manager.GetString(nameof(CadReferenceFilename), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Upload must contain between 1 and {0} files.
    /// </summary>
    public static string CadUploadFileCount => Manager.GetString(nameof(CadUploadFileCount), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Each uploaded file must have a safe .a3d or .m3d file name.
    /// </summary>
    public static string CadUploadSafeFile => Manager.GetString(nameof(CadUploadSafeFile), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Duplicate file name: {0}
    /// </summary>
    public static string CadUploadDuplicate => Manager.GetString(nameof(CadUploadDuplicate), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Upload exceeds the configured size limit.
    /// </summary>
    public static string CadUploadExceeded => Manager.GetString(nameof(CadUploadExceeded), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Invalid input.
    /// </summary>
    public static string ProblemTitle => Manager.GetString(nameof(ProblemTitle), CultureInfo.CurrentUICulture)!;

}
