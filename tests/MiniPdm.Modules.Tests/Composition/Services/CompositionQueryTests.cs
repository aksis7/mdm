using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Modules.Composition.DtoModels;
using MiniPdm.Modules.Composition.Services;
using Xunit;

namespace MiniPdm.Modules.Tests.Composition.Services;

/// <summary>
/// Проверяет построение представления состава изделия и диагностику циклов и отсутствующих версий.
/// </summary>
public sealed class CompositionQueryTests
{
    /// <summary>
    /// Проверяет отображение отдельных путей вхождения для ромбовидного состава.
    /// </summary>
    [Fact]
    public void MapsDistinctOccurrencePathsInDiamond()
    {
        var root = Guid.NewGuid();
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();
        var leaf = Guid.NewGuid();
        var occurrences = new CompositionOccurrenceDto[]
        {
            Occurrence(root, [root], null, PdmObjectType.Assembly, Guid.NewGuid()),
            Occurrence(left, [root, left], [root], PdmObjectType.Assembly, Guid.NewGuid()),
            Occurrence(right, [root, right], [root], PdmObjectType.Assembly, Guid.NewGuid()),
            Occurrence(leaf, [root, left, leaf], [root, left], PdmObjectType.Part, Guid.NewGuid()),
            Occurrence(leaf, [root, right, leaf], [root, right], PdmObjectType.Part, Guid.NewGuid())
        };
        var tree = CompositionReadService.MapTreeDto(root, occurrences);
        Assert.Equal(root, tree.RootObjectId);
        var leafNodes = tree.Nodes.Where(node => node.ObjectId == leaf).ToArray();
        Assert.Equal(2, leafNodes.Length);
        Assert.NotEqual(leafNodes[0].ObjectPath, leafNodes[1].ObjectPath);
        Assert.Equal(new[] { root, left, leaf }, leafNodes[0].ObjectPath);
        Assert.Equal(new[] { root, right, leaf }, leafNodes[1].ObjectPath);
        Assert.All(leafNodes, node => Assert.Equal("Part", node.Type));
    }

    /// <summary>
    /// Проверяет отображение состава, содержащего только корневой объект.
    /// </summary>
    [Fact]
    public void MapsRootOnlyComposition()
    {
        var id = Guid.NewGuid();
        var tree = CompositionReadService.MapTreeDto(id, [Occurrence(id, [id], null, PdmObjectType.Part, Guid.NewGuid())]);
        Assert.Equal(id, tree.RootObjectId);
        Assert.Single(tree.Nodes);
    }

    /// <summary>
    /// Проверяет отображение узла без текущей версии с соответствующей диагностикой.
    /// </summary>
    [Fact]
    public void MapsNodeWithoutCurrentVersionWithDiagnostic()
    {
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var query = new CompositionOccurrenceDto[]
        {
            Occurrence(root, [root], null, PdmObjectType.Assembly, Guid.NewGuid()),
            Occurrence(child, [root, child], [root], PdmObjectType.Part, null, name: "Missing version")
        };

        var tree = CompositionReadService.MapTreeDto(root, query);
        var node = Assert.Single(tree.Nodes, node => node.ObjectId == child);
        Assert.Equal("NoCurrentVersion", node.ErrorCode);
        Assert.NotNull(node.Error);
        Assert.Null(node.VersionId);
        Assert.Equal("Missing version", node.Name);
    }

    /// <summary>
    /// Проверяет сообщение о цикле с путём вхождения, на котором он обнаружен.
    /// </summary>
    [Fact]
    public void MapsCycleWithTheOccurrencePath()
    {
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var cyclePath = new[] { root, child, root };
        var query = new CompositionOccurrenceDto[]
        {
            Occurrence(root, [root], null, PdmObjectType.Assembly, Guid.NewGuid()),
            Occurrence(child, [root, child], [root], PdmObjectType.Assembly, Guid.NewGuid()),
            Occurrence(root, cyclePath, [root, child], PdmObjectType.Assembly, Guid.NewGuid(), isCycle: true)
        };

        var tree = CompositionReadService.MapTreeDto(root, query);
        var cycle = Assert.Single(tree.Nodes, node => node.ErrorCode == "Cycle");
        Assert.Equal(cyclePath, cycle.ObjectPath);
        Assert.Contains(string.Join(" → ", cyclePath), cycle.Error!);
    }

    private static CompositionOccurrenceDto Occurrence(
        Guid id,
        Guid[] path,
        Guid[]? parentPath,
        PdmObjectType type,
        Guid? versionId,
        bool isCycle = false,
        string? name = "Sample") => new CompositionOccurrenceDto
        {
            ObjectId = id,
            ObjectPath = path,
            ParentPath = parentPath,
            LocalQuantity = 1,
            Type = type,
            Designation = type == PdmObjectType.StandardPart ? null : "АБВГ.301245.001",
            Name = name,
            Material = "Steel",
            VersionId = versionId,
            VersionNumber = versionId is null ? null : 1,
            State = versionId is null ? null : VersionState.InWork,
            UnitMassKg = type == PdmObjectType.Assembly ? null : 2.5m,
            IsCycle = isCycle
        };

}
