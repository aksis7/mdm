using System.Globalization;
using System.Resources;

namespace MiniPdm.Common.Resources;

/// <summary>
/// Тексты сообщений о нарушении бизнес-правил.
/// </summary>
public static class BusinessLogicException
{
    private static readonly ResourceManager Manager = new("MiniPdm.Common.Resources.BusinessLogicException", typeof(BusinessLogicException).Assembly);
    /// <summary>
    /// Возвращает текст сообщения: Unknown PDM object type.
    /// </summary>
    public static string ModelObjectInvalid => Manager.GetString(nameof(ModelObjectInvalid), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Changing a standard part name requires creating a new object.
    /// </summary>
    public static string StandardPartNameImmutable => Manager.GetString(nameof(StandardPartNameImmutable), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The selected version was not found for this object.
    /// </summary>
    public static string VersionNotFound => Manager.GetString(nameof(VersionNotFound), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The next version number exceeds Int32.
    /// </summary>
    public static string VersionOverflow => Manager.GetString(nameof(VersionOverflow), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Component object '{0}' does not exist.
    /// </summary>
    public static string UnknownChildObject => Manager.GetString(nameof(UnknownChildObject), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The version is already in the requested state.
    /// </summary>
    public static string VersionStateUnchanged => Manager.GetString(nameof(VersionStateUnchanged), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The requested version state transition is not allowed.
    /// </summary>
    public static string InvalidVersionStateTransition => Manager.GetString(nameof(InvalidVersionStateTransition), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Approved and cancelled versions cannot be edited.
    /// </summary>
    public static string VersionImmutable => Manager.GetString(nameof(VersionImmutable), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The proposed active composition would create a cycle.
    /// </summary>
    public static string CompositionCycle => Manager.GetString(nameof(CompositionCycle), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The current database composition graph already contains a cycle; import is blocked.
    /// </summary>
    public static string CompositionCycleInDatabase => Manager.GetString(nameof(CompositionCycleInDatabase), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The composition path repeats an object and cannot be included in the calculation.
    /// </summary>
    public static string CompositionPathRepeated => Manager.GetString(nameof(CompositionPathRepeated), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The quantity along this composition path exceeds the supported decimal range.
    /// </summary>
    public static string QuantityOverflow => Manager.GetString(nameof(QuantityOverflow), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The object has no current non-cancelled version.
    /// </summary>
    public static string NoCurrentVersion => Manager.GetString(nameof(NoCurrentVersion), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The current version does not have a unit mass.
    /// </summary>
    public static string MissingMass => Manager.GetString(nameof(MissingMass), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The total mass for this specification item exceeds the supported decimal range.
    /// </summary>
    public static string SpecificationMassOverflow => Manager.GetString(nameof(SpecificationMassOverflow), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The calculated assembly mass exceeds the supported decimal range.
    /// </summary>
    public static string AssemblyMassOverflow => Manager.GetString(nameof(AssemblyMassOverflow), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The combined specification quantity exceeds the supported decimal range.
    /// </summary>
    public static string SpecificationQuantityOverflow => Manager.GetString(nameof(SpecificationQuantityOverflow), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The combined count for component '{0}' exceeds Int32.
    /// </summary>
    public static string ComponentCountOverflow => Manager.GetString(nameof(ComponentCountOverflow), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Repeated component rows were combined.
    /// </summary>
    public static string RepeatedComponentRowsCombined => Manager.GetString(nameof(RepeatedComponentRowsCombined), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Repeated component rows were combined by file name.
    /// </summary>
    public static string RepeatedComponentRowsCombinedByFile => Manager.GetString(nameof(RepeatedComponentRowsCombinedByFile), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The background task is already running.
    /// </summary>
    public static string TaskAlreadyRunning => Manager.GetString(nameof(TaskAlreadyRunning), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Standard parts must not have a designation.
    /// </summary>
    public static string StandardPartDesignationInvalid => Manager.GetString(nameof(StandardPartDesignationInvalid), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: A component file name is empty.
    /// </summary>
    public static string ComponentFilenameRequired => Manager.GetString(nameof(ComponentFilenameRequired), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The package component graph contains a cycle.
    /// </summary>
    public static string PackageCycle => Manager.GetString(nameof(PackageCycle), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Business rule conflict.
    /// </summary>
    public static string ProblemTitle => Manager.GetString(nameof(ProblemTitle), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Part mass is missing.
    /// </summary>
    public static string PartMassMissing => Manager.GetString(nameof(PartMassMissing), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Part mass is zero.
    /// </summary>
    public static string PartMassZero => Manager.GetString(nameof(PartMassZero), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Standard part mass is zero.
    /// </summary>
    public static string StandardPartMassZero => Manager.GetString(nameof(StandardPartMassZero), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The package contains duplicate file names.
    /// </summary>
    public static string ImportDuplicateNames => Manager.GetString(nameof(ImportDuplicateNames), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The CAD reader returned no document.
    /// </summary>
    public static string ImportMissingDocument => Manager.GetString(nameof(ImportMissingDocument), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: More than one package document has this PDM identity.
    /// </summary>
    public static string ImportDuplicateIdentity => Manager.GetString(nameof(ImportDuplicateIdentity), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: This PDM identity is already used by a different object type.
    /// </summary>
    public static string IdentityTypeConflict => Manager.GetString(nameof(IdentityTypeConflict), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Component file '{0}' is unavailable for import.
    /// </summary>
    public static string ImportComponentUnavailable => Manager.GetString(nameof(ImportComponentUnavailable), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: Component file '{0}' was rejected or could not be resolved.
    /// </summary>
    public static string ImportComponentRejected => Manager.GetString(nameof(ImportComponentRejected), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The version mutation could not be completed.
    /// </summary>
    public static string VersionMutationFailed => Manager.GetString(nameof(VersionMutationFailed), CultureInfo.CurrentUICulture)!;

    /// <summary>
    /// Возвращает текст сообщения: The object has no current non-cancelled version.
    /// </summary>
    public static string CurrentVersionMissing => Manager.GetString(nameof(CurrentVersionMissing), CultureInfo.CurrentUICulture)!;

}
