using Resources = MiniPdm.Common.Resources;
using MiniPdm.Domain.Objects;

namespace MiniPdm.Domain.Versions;

/// <summary>
///     Результаты проверки атрибутов предлагаемой версии объекта.
/// </summary>
public sealed record VersionAttributeValidation(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    /// <summary>
    ///     Значения, нарушающие ограничения типа объекта или хранилища.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Errors;

    /// <summary>
    ///     Допустимые значения, на которые следует обратить внимание оператора.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Warnings;

    /// <summary>
    ///     Показывает, завершилась ли проверка без ошибок.
    /// </summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
///     Проверяет атрибуты версии с учётом типа объекта PDM.
/// </summary>
public static class VersionAttributeRules
{
    /// <summary>
    ///     Проверяет наименование, материал и массу по правилам типа объекта и ограничениям базы данных.
    /// </summary>
    /// <param name="type">
    ///     Тип проверяемого объекта.
    /// </param>
    /// <param name="name">
    ///     Наименование версии или стандартного изделия.
    /// </param>
    /// <param name="material">
    ///     Материал детали, если применимо.
    /// </param>
    /// <param name="mass">
    ///     Масса одного изделия в килограммах, если применимо.
    /// </param>
    /// <returns>
    ///     Ошибки проверки и предупреждения, не препятствующие сохранению.
    /// </returns>
    public static VersionAttributeValidation Validate(PdmObjectType type, string? name, string? material, decimal? mass)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(name))
            errors.Add(Resources.InputLogicException.PayloadNameRequired);
        if (name?.Length > 512)
            errors.Add(Resources.InputLogicException.PayloadNameTooLong);
        if (material?.Length > 256)
            errors.Add(Resources.InputLogicException.MaterialTooLong);
        if (type == PdmObjectType.StandardPart && name is not null && ObjectIdentity.NormalizeStandardName(name).Length > 512)
            errors.Add(Resources.InputLogicException.StandardNameTooLong);
        if (mass is < 0)
            errors.Add(Resources.InputLogicException.MassNegative);
        if (mass is { } value && (value > 999999999999.999999m || decimal.Round(value, 6) != value))
            errors.Add(Resources.InputLogicException.MassOutOfRange);

        switch (type)
        {
            case PdmObjectType.Assembly:
                if (material is not null || mass is not null)
                    errors.Add(Resources.InputLogicException.AssemblyAttributesInvalid);
                break;
            case PdmObjectType.Part:
                if (string.IsNullOrWhiteSpace(material))
                    errors.Add(Resources.InputLogicException.PartMaterialRequired);
                if (mass is null)
                    warnings.Add(Resources.BusinessLogicException.PartMassMissing);
                else if (mass == 0)
                    warnings.Add(Resources.BusinessLogicException.PartMassZero);
                break;
            case PdmObjectType.StandardPart:
                if (material is not null)
                    errors.Add(Resources.InputLogicException.StandardMaterialInvalid);
                if (mass is null)
                    errors.Add(Resources.InputLogicException.StandardMassRequired);
                else if (mass == 0)
                    warnings.Add(Resources.BusinessLogicException.StandardPartMassZero);
                break;
            default:
                errors.Add(Resources.BusinessLogicException.ModelObjectInvalid);
                break;
        }
        return new VersionAttributeValidation(errors, warnings);
    }
}
