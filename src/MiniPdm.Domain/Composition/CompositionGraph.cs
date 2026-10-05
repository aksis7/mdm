namespace MiniPdm.Domain.Composition;

/// <summary>
///     Ориентированная связь от родительского объекта к дочернему в графе состава.
/// </summary>
public sealed record CompositionGraphEdge(Guid ParentId, Guid ChildId)
{
    /// <summary>
    ///     Идентификатор сборки, содержащей компонент.
    /// </summary>
    public Guid ParentId { get; init; } = ParentId;

    /// <summary>
    ///     Идентификатор объекта-компонента.
    /// </summary>
    public Guid ChildId { get; init; } = ChildId;
}

/// <summary>
/// Проверяет ориентированный граф состава и определяет узлы, участвующие в циклах.
/// </summary>
public static class CompositionGraph
{
    /// <summary>
    ///     Находит один цикл в заданном ориентированном графе.
    /// </summary>
    /// <param name="edges">
    ///     Связи от родительских объектов к дочерним для проверки.
    /// </param>
    /// <returns>
    ///     Замкнутый путь с совпадающими первым и последним идентификаторами либо null, если циклов нет.
    /// </returns>
    public static Guid[]? FindCyclePath(IEnumerable<CompositionGraphEdge> edges)
    {
        var adjacency = edges.GroupBy(x => x.ParentId)
            .ToDictionary(x => x.Key, x => x.Select(e => e.ChildId).Distinct().ToArray());
        var state = new Dictionary<Guid, byte>();
        var stack = new List<Guid>();
        var positions = new Dictionary<Guid, int>();
        foreach (var node in adjacency.Keys.Concat(adjacency.Values.SelectMany(x => x)).Distinct())
        {
            var path = Visit(node);
            if (path is not null)
                return path;
        }
        return null;

        Guid[]? Visit(Guid node)
        {
            if (state.TryGetValue(node, out var currentState))
                return null;
            state[node] = 1;
            positions[node] = stack.Count;
            stack.Add(node);
            if (adjacency.TryGetValue(node, out var children))
                foreach (var child in children)
                {
                    if (state.TryGetValue(child, out var childState) && childState == 1)
                        return stack.Skip(positions[child]).Append(child).ToArray();
                    if (childState == 0)
                    {
                        var path = Visit(child);
                        if (path is not null)
                            return path;
                    }
                }
            stack.RemoveAt(stack.Count - 1);
            positions.Remove(node);
            state[node] = 2;
            return null;
        }
    }

    /// <summary>
    ///     Находит узлы, из которых можно вернуться к ним же, пройдя по одной или нескольким заданным связям.
    /// </summary>
    /// <param name="edges">
    ///     Связи от родительских объектов к дочерним для проверки.
    /// </param>
    /// <returns>
    ///     Идентификаторы узлов, входящих хотя бы в один ориентированный цикл.
    /// </returns>
    public static IReadOnlySet<Guid> FindCycleNodes(IEnumerable<CompositionGraphEdge> edges)
    {
        var edgeArray = edges.ToArray();
        var adjacency = edgeArray.GroupBy(x => x.ParentId)
            .ToDictionary(x => x.Key, x => x.Select(e => e.ChildId).Distinct().ToArray());
        var nodes = edgeArray.SelectMany(x => new[] { x.ParentId, x.ChildId }).Distinct().ToArray();
        var cyclic = new HashSet<Guid>();
        foreach (var start in nodes)
        {
            var seen = new HashSet<Guid>();
            var pending = new Stack<Guid>();
            if (adjacency.TryGetValue(start, out var children))
                foreach (var child in children)
                    pending.Push(child);
            while (pending.TryPop(out var current))
            {
                if (current == start)
                {
                    cyclic.Add(start);
                    break;
                }
                if (!seen.Add(current))
                    continue;
                if (adjacency.TryGetValue(current, out var next))
                    foreach (var child in next)
                        pending.Push(child);
            }
        }
        return cyclic;
    }
}
