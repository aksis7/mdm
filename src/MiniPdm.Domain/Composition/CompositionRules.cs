using Resources = MiniPdm.Common.Resources;
namespace MiniPdm.Domain.Composition;

/// <summary>
///     Нормализованные количества компонентов с ошибками проверки и информационными предупреждениями.
/// </summary>
/// <typeparam name="TKey">
///     Тип ключа, однозначно обозначающего объект-компонент.
/// </typeparam>
public sealed record CompositionNormalization<TKey>(IReadOnlyDictionary<TKey, int> Items,
    IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings) where TKey : notnull
{
    /// <summary>
    ///     Уникальные ключи компонентов и суммарные количества.
    /// </summary>
    public IReadOnlyDictionary<TKey, int> Items { get; init; } = Items;

    /// <summary>
    ///     Ошибки неверного количества или переполнения.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Errors;

    /// <summary>
    ///     Предупреждения о допустимых изменениях при нормализации данных.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Warnings;
}

/// <summary>
/// Проверяет строки состава и объединяет повторные вхождения одного компонента.
/// </summary>
public static class CompositionRules
{
    /// <summary>
    ///     Проверяет положительность количеств и объединяет повторяющиеся ключи компонентов.
    /// </summary>
    /// <typeparam name="TKey">
    ///     Тип ключа, однозначно обозначающего объект-компонент.
    /// </typeparam>
    /// <param name="rows">
    ///     Ключи компонентов с заданными для них количествами.
    /// </param>
    /// <returns>
    ///     Уникальные количества компонентов, ошибки и предупреждения.
    /// </returns>
    public static CompositionNormalization<TKey> Normalize<TKey>(IEnumerable<(TKey Child, int Quantity)> rows)
        where TKey : notnull
    {
        var items = new Dictionary<TKey, int>();
        var errors = new List<string>();
        var repeatedValidRows = false;
        foreach (var (child, quantity) in rows)
        {
            if (quantity <= 0)
            {
                errors.Add(string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.InputLogicException.ComponentCountPositive, child));
                continue;
            }

            if (items.TryGetValue(child, out var existing))
            {
                repeatedValidRows = true;
                try
                {
                    items[child] = checked(existing + quantity);
                }
                catch (OverflowException)
                {
                    errors.Add(string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.BusinessLogicException.ComponentCountOverflow, child));
                    items.Remove(child);
                }
            }
            else
                items.Add(child, quantity);
        }

        return new CompositionNormalization<TKey>(items, errors,
            repeatedValidRows && errors.Count == 0 ? [Resources.BusinessLogicException.RepeatedComponentRowsCombined] : []);
    }
}
