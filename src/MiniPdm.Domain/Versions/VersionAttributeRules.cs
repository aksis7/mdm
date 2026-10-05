using Resources = MiniPdm.Common.Resources;
using MiniPdm.Domain.Objects;

namespace MiniPdm.Domain.Versions;

/// <summary>
/// Код проверяемого правила атрибутов версии.
/// </summary>
public enum VersionAttributeRule
{
    /// <summary>
    /// Наименование не задано.
    /// </summary>
    PayloadNameRequired,
    /// <summary>
    /// Наименование длиннее допустимого.
    /// </summary>
    PayloadNameTooLong,
    /// <summary>
    /// Материал длиннее допустимого.
    /// </summary>
    MaterialTooLong,
    /// <summary>
    /// Нормализованное наименование стандартного изделия длиннее допустимого.
    /// </summary>
    StandardNameTooLong,
    /// <summary>
    /// Масса отрицательна.
    /// </summary>
    MassNegative,
    /// <summary>
    /// Масса выходит за допустимый диапазон или точность.
    /// </summary>
    MassOutOfRange,
    /// <summary>
    /// У сборки заданы недопустимые атрибуты материала или массы.
    /// </summary>
    AssemblyAttributesInvalid,
    /// <summary>
    /// У детали не задан материал.
    /// </summary>
    PartMaterialRequired,
    /// <summary>
    /// У детали не задана масса.
    /// </summary>
    PartMassMissing,
    /// <summary>
    /// Масса детали равна нулю.
    /// </summary>
    PartMassZero,
    /// <summary>
    /// У стандартного изделия задан недопустимый материал.
    /// </summary>
    StandardMaterialInvalid,
    /// <summary>
    /// У стандартного изделия не задана масса.
    /// </summary>
    StandardMassRequired,
    /// <summary>
    /// Масса стандартного изделия равна нулю.
    /// </summary>
    StandardPartMassZero,
    /// <summary>
    /// Задан неизвестный тип объекта.
    /// </summary>
    ModelObjectInvalid
}

/// <summary>
/// Область, в которой применяется правило атрибутов версии.
/// </summary>
public enum VersionAttributeRuleScope
{
    /// <summary>
    /// Общие ограничения на значения атрибутов.
    /// </summary>
    Common,
    /// <summary>
    /// Ограничения, зависящие от типа объекта.
    /// </summary>
    TypeSpecific
}

/// <summary>
/// Одно нарушение или предупреждение, найденное при проверке атрибутов версии.
/// </summary>
public sealed record VersionAttributeIssue
{
    /// <summary>
    /// Создаёт структурированное сообщение о результате проверки.
    /// </summary>
    /// <param name="code">Код сработавшего правила.</param>
    /// <param name="scope">Область применения правила.</param>
    /// <param name="message">Готовый текст из ресурсов сообщений.</param>
    public VersionAttributeIssue(VersionAttributeRule code, VersionAttributeRuleScope scope, string message)
    {
        Code = code;
        Scope = scope;
        Message = message;
    }

    /// <summary>
    /// Код сработавшего правила.
    /// </summary>
    public VersionAttributeRule Code
    {
        get;
    }

    /// <summary>
    /// Область применения правила.
    /// </summary>
    public VersionAttributeRuleScope Scope
    {
        get;
    }

    /// <summary>
    /// Готовый текст из ресурсов сообщений.
    /// </summary>
    public string Message
    {
        get;
    }
}

/// <summary>
/// Результаты проверки атрибутов предлагаемой версии объекта.
/// </summary>
public sealed record VersionAttributeValidation
{
    /// <summary>
    /// Создаёт результат проверки.
    /// </summary>
    /// <param name="errors">Нарушения, запрещающие сохранить значения.</param>
    /// <param name="warnings">Замечания, не препятствующие сохранению.</param>
    public VersionAttributeValidation(IReadOnlyList<VersionAttributeIssue> errors,
        IReadOnlyList<VersionAttributeIssue> warnings)
    {
        Errors = errors;
        Warnings = warnings;
    }

    /// <summary>
    /// Нарушения, запрещающие сохранить значения.
    /// </summary>
    public IReadOnlyList<VersionAttributeIssue> Errors
    {
        get;
    }

    /// <summary>
    /// Замечания, не препятствующие сохранению.
    /// </summary>
    public IReadOnlyList<VersionAttributeIssue> Warnings
    {
        get;
    }

    /// <summary>
    /// Показывает, завершилась ли проверка без ошибок.
    /// </summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Проверяет атрибуты версии с учётом типа объекта PDM.
/// </summary>
public static class VersionAttributeRules
{
    /// <summary>
    /// Проверяет наименование, материал и массу по правилам типа объекта и ограничениям базы данных.
    /// </summary>
    /// <param name="type">Тип проверяемого объекта.</param>
    /// <param name="name">Наименование версии или стандартного изделия.</param>
    /// <param name="material">Материал детали, если применимо.</param>
    /// <param name="mass">Масса одного изделия в килограммах, если применимо.</param>
    /// <returns>
    /// Структурированные ошибки проверки и предупреждения.
    /// </returns>
    public static VersionAttributeValidation Validate(PdmObjectType type, string? name, string? material, decimal? mass)
    {
        var errors = new List<VersionAttributeIssue>();
        var warnings = new List<VersionAttributeIssue>();

        ValidateCommon(type, name, material, mass, errors);

        switch (type)
        {
            case PdmObjectType.Assembly:
                ValidateAssembly(material, mass, errors);
                break;
            case PdmObjectType.Part:
                ValidatePart(material, mass, errors, warnings);
                break;
            case PdmObjectType.StandardPart:
                ValidateStandardPart(material, mass, errors, warnings);
                break;
            default:
                AddError(errors, VersionAttributeRule.ModelObjectInvalid,
                    VersionAttributeRuleScope.TypeSpecific, Resources.BusinessLogicException.ModelObjectInvalid);
                break;
        }

        return new VersionAttributeValidation(errors, warnings);
    }

    private static void ValidateCommon(PdmObjectType type, string? name, string? material, decimal? mass,
        List<VersionAttributeIssue> errors)
    {
        if (string.IsNullOrWhiteSpace(name))
            AddError(errors, VersionAttributeRule.PayloadNameRequired, VersionAttributeRuleScope.Common,
                Resources.InputLogicException.PayloadNameRequired);
        if (name?.Length > 512)
            AddError(errors, VersionAttributeRule.PayloadNameTooLong, VersionAttributeRuleScope.Common,
                Resources.InputLogicException.PayloadNameTooLong);
        if (material?.Length > 256)
            AddError(errors, VersionAttributeRule.MaterialTooLong, VersionAttributeRuleScope.Common,
                Resources.InputLogicException.MaterialTooLong);
        if (type == PdmObjectType.StandardPart && name is not null
            && ObjectIdentity.NormalizeStandardName(name).Length > 512)
            AddError(errors, VersionAttributeRule.StandardNameTooLong, VersionAttributeRuleScope.Common,
                Resources.InputLogicException.StandardNameTooLong);
        if (mass is < 0)
            AddError(errors, VersionAttributeRule.MassNegative, VersionAttributeRuleScope.Common,
                Resources.InputLogicException.MassNegative);
        if (mass is { } value && (value > 999999999999.999999m || decimal.Round(value, 6) != value))
            AddError(errors, VersionAttributeRule.MassOutOfRange, VersionAttributeRuleScope.Common,
                Resources.InputLogicException.MassOutOfRange);
    }

    private static void ValidateAssembly(string? material, decimal? mass, List<VersionAttributeIssue> errors)
    {
        if (material is not null || mass is not null)
            AddError(errors, VersionAttributeRule.AssemblyAttributesInvalid,
                VersionAttributeRuleScope.TypeSpecific, Resources.InputLogicException.AssemblyAttributesInvalid);
    }

    private static void ValidatePart(string? material, decimal? mass, List<VersionAttributeIssue> errors,
        List<VersionAttributeIssue> warnings)
    {
        if (string.IsNullOrWhiteSpace(material))
            AddError(errors, VersionAttributeRule.PartMaterialRequired, VersionAttributeRuleScope.TypeSpecific,
                Resources.InputLogicException.PartMaterialRequired);
        if (mass is null)
            AddWarning(warnings, VersionAttributeRule.PartMassMissing, VersionAttributeRuleScope.TypeSpecific,
                Resources.BusinessLogicException.PartMassMissing);
        else if (mass == 0)
            AddWarning(warnings, VersionAttributeRule.PartMassZero, VersionAttributeRuleScope.TypeSpecific,
                Resources.BusinessLogicException.PartMassZero);
    }

    private static void ValidateStandardPart(string? material, decimal? mass, List<VersionAttributeIssue> errors,
        List<VersionAttributeIssue> warnings)
    {
        if (material is not null)
            AddError(errors, VersionAttributeRule.StandardMaterialInvalid,
                VersionAttributeRuleScope.TypeSpecific, Resources.InputLogicException.StandardMaterialInvalid);
        if (mass is null)
            AddError(errors, VersionAttributeRule.StandardMassRequired, VersionAttributeRuleScope.TypeSpecific,
                Resources.InputLogicException.StandardMassRequired);
        else if (mass == 0)
            AddWarning(warnings, VersionAttributeRule.StandardPartMassZero,
                VersionAttributeRuleScope.TypeSpecific, Resources.BusinessLogicException.StandardPartMassZero);
    }

    private static void AddError(List<VersionAttributeIssue> errors, VersionAttributeRule code,
        VersionAttributeRuleScope scope, string message) => errors.Add(new VersionAttributeIssue(code, scope, message));

    private static void AddWarning(List<VersionAttributeIssue> warnings, VersionAttributeRule code,
        VersionAttributeRuleScope scope, string message) => warnings.Add(new VersionAttributeIssue(code, scope, message));
}
