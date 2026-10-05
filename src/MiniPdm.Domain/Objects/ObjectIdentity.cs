using System.Text.RegularExpressions;

namespace MiniPdm.Domain.Objects;

/// <summary>
/// Проверяет обозначения PDM и формирует ключ сравнения наименований стандартных изделий.
/// </summary>
public static partial class ObjectIdentity
{
    [GeneratedRegex(@"\A[А-ЯЁ]{4}\.[0-9]{6}\.[0-9]{3}\z", RegexOptions.CultureInvariant)]
    private static partial Regex DesignationPattern();

    /// <summary>
    ///     Проверяет, что обозначение содержит четыре заглавные кириллические буквы и группы цифр заданной длины.
    /// </summary>
    /// <param name="designation">
    ///     Обозначение для проверки.
    /// </param>
    /// <returns>
    ///     <see langword="true"/>, если значение соответствует формату обозначения проекта.
    /// </returns>
    public static bool IsValidDesignation(string designation) => DesignationPattern().IsMatch(designation);

    /// <summary>
    ///     Формирует ключ для поиска совпадающих наименований стандартных изделий.
    /// </summary>
    /// <param name="name">
    ///     Наименование для обрезки пробелов, нормализации пробелов и сравнения без учёта регистра.
    /// </param>
    /// <returns>
    ///     Нормализованное наименование в верхнем регистре.
    /// </returns>
    public static string NormalizeStandardName(string name) =>
        Regex.Replace(name.Trim(), @"\s+", " ").ToUpperInvariant();
}
