using MiniPdm.Domain.Calculations;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using Xunit;

namespace MiniPdm.Modules.Tests.Calculations.Features.Queries.GetCompositionCalculation;

/// <summary>
/// Проверяет расчёт количеств, масс и спецификации для состава изделия, включая неполные и циклические графы.
/// </summary>
public sealed class CompositionCalculationTests
{
    /// <summary>
    /// Проверяет подсчёт количеств по всем путям ромбовидного состава и группировку по объекту.
    /// </summary>
    [Fact]
    public void CalculatesDiamondQuantitiesAcrossPathsAndGroupsByObject()
    {
        var root = Guid.NewGuid();
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();
        var part = Guid.NewGuid();
        var occurrences = new[]
        {
            Node(root, [root], null, 1, PdmObjectType.Assembly),
            Node(left, [root, left], [root], 2, PdmObjectType.Assembly),
            Node(right, [root, right], [root], 5, PdmObjectType.Assembly),
            Node(part, [root, left, part], [root, left], 3, PdmObjectType.Part, mass: 2m),
            Node(part, [root, right, part], [root, right], 5, PdmObjectType.Part, mass: 2m)
        };

        var result = CompositionCalculator.Calculate(root, occurrences);

        Assert.True(result.IsComplete);
        Assert.Equal(62m, result.TotalMassKg);
        var item = Assert.Single(result.Items);
        Assert.Equal(31m, item.Quantity);
        Assert.Equal(2m, item.UnitMassKg);
        Assert.Equal(62m, item.TotalMassKg);
    }

    /// <summary>
    /// Проверяет сохранение известного количества и блокировку расчёта массы сборки при отсутствующей массе детали.
    /// </summary>
    [Fact]
    public void MissingMassKeepsKnownQuantityAndBlocksAssemblyMass()
    {
        var root = Guid.NewGuid();
        var part = Guid.NewGuid();
        var result = CompositionCalculator.Calculate(root,
        [
            Node(root, [root], null, 1, PdmObjectType.Assembly),
            Node(part, [root, part], [root], 4, PdmObjectType.Part, mass: null)
        ]);

        Assert.False(result.IsComplete);
        Assert.Null(result.TotalMassKg);
        Assert.Equal("MissingMass", Assert.Single(result.Diagnostics).Code);
        Assert.Equal(4m, Assert.Single(result.Items).Quantity);
        Assert.Null(Assert.Single(result.Items).TotalMassKg);
    }

    /// <summary>
    /// Проверяет блокировку расчёта сборки без текущей версии и сохранение известного количества листовой детали.
    /// </summary>
    [Fact]
    public void MissingCurrentVersionBlocksAssemblyAndPreservesKnownLeafQuantity()
    {
        var root = Guid.NewGuid();
        var rootResult = CompositionCalculator.Calculate(root,
            [Node(root, [root], null, 1, PdmObjectType.Assembly, hasVersion: false)]);

        Assert.False(rootResult.IsComplete);
        Assert.Null(rootResult.TotalMassKg);
        Assert.Equal("NoCurrentVersion", Assert.Single(rootResult.Diagnostics).Code);

        var part = Guid.NewGuid();
        var leafResult = CompositionCalculator.Calculate(root,
        [
            Node(root, [root], null, 1, PdmObjectType.Assembly),
            Node(part, [root, part], [root], 7, PdmObjectType.Part, mass: 3m, hasVersion: false)
        ]);

        Assert.False(leafResult.IsComplete);
        Assert.Null(leafResult.TotalMassKg);
        Assert.Equal("NoCurrentVersion", Assert.Single(leafResult.Diagnostics).Code);
        var leaf = Assert.Single(leafResult.Items);
        Assert.Equal(7m, leaf.Quantity);
        Assert.Null(leaf.UnitMassKg);
        Assert.Null(leaf.TotalMassKg);
    }

    /// <summary>
    /// Проверяет включение диагностики цикла и исключение циклического вхождения из спецификации.
    /// </summary>
    [Fact]
    public void CycleIsReportedAndCyclicOccurrenceIsExcludedFromSpecification()
    {
        var root = Guid.NewGuid();
        var part = Guid.NewGuid();
        var result = CompositionCalculator.Calculate(root,
        [
            Node(root, [root], null, 1, PdmObjectType.Assembly),
            Node(part, [root, part], [root], 2, PdmObjectType.Part, mass: 3m),
            Node(root, [root, root], [root], 1, PdmObjectType.Assembly, isCycle: true)
        ]);

        Assert.False(result.IsComplete);
        Assert.Null(result.TotalMassKg);
        Assert.Equal("Cycle", Assert.Single(result.Diagnostics).Code);
        Assert.Equal(part, Assert.Single(result.Items).ObjectId);
        Assert.Equal(6m, Assert.Single(result.Items).TotalMassKg);
    }

    /// <summary>
    /// Проверяет нулевую массу и пустую спецификацию для пустой сборки.
    /// </summary>
    [Fact]
    public void EmptyAssemblyHasZeroMassAndEmptySpecification()
    {
        var root = Guid.NewGuid();
        var result = CompositionCalculator.Calculate(root, [Node(root, [root], null, 1, PdmObjectType.Assembly)]);

        Assert.True(result.IsComplete);
        Assert.Equal(0m, result.TotalMassKg);
        Assert.Empty(result.Items);
        Assert.Empty(result.Diagnostics);
    }

    /// <summary>
    /// Проверяет включение корневой детали в спецификацию с количеством один.
    /// </summary>
    [Fact]
    public void RootPartAppearsWithQuantityOne()
    {
        var root = Guid.NewGuid();
        var result = CompositionCalculator.Calculate(root, [Node(root, [root], null, 1, PdmObjectType.Part, mass: 5m)]);

        Assert.True(result.IsComplete);
        Assert.Equal(5m, result.TotalMassKg);
        var item = Assert.Single(result.Items);
        Assert.Equal(1m, item.Quantity);
        Assert.Equal(5m, item.TotalMassKg);
    }

    /// <summary>
    /// Проверяет группировку вхождений стандартной детали по идентификатору объекта.
    /// </summary>
    [Fact]
    public void StandardPartOccurrencesAreGroupedByObjectId()
    {
        var root = Guid.NewGuid();
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();
        var standardPart = Guid.NewGuid();
        var result = CompositionCalculator.Calculate(root,
        [
            Node(root, [root], null, 1, PdmObjectType.Assembly),
            Node(left, [root, left], [root], 1, PdmObjectType.Assembly),
            Node(right, [root, right], [root], 1, PdmObjectType.Assembly),
            Node(standardPart, [root, left, standardPart], [root, left], 2, PdmObjectType.StandardPart, mass: 0.5m),
            Node(standardPart, [root, right, standardPart], [root, right], 3, PdmObjectType.StandardPart, mass: 0.5m)
        ]);

        Assert.True(result.IsComplete);
        Assert.Equal(2.5m, result.TotalMassKg);
        var item = Assert.Single(result.Items);
        Assert.Equal(PdmObjectType.StandardPart, item.Type);
        Assert.Equal(5m, item.Quantity);
    }

    /// <summary>
    /// Проверяет распространение переполнения количества по пути без повторных диагностик.
    /// </summary>
    [Fact]
    public void QuantityOverflowOnPathPropagatesWithoutRedundantDiagnostics()
    {
        var root = Guid.NewGuid();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).Prepend(root).ToArray();
        var nodes = new List<CompositionCalculationInput> { Node(root, [root], null, 1, PdmObjectType.Assembly) };
        for (var index = 1; index < ids.Length; index++)
        {
            var path = ids.Take(index + 1).ToArray();
            var parent = ids.Take(index).ToArray();
            nodes.Add(Node(ids[index], path, parent, int.MaxValue,
                index == ids.Length - 1 ? PdmObjectType.Part : PdmObjectType.Assembly,
                mass: 2m));
        }

        var result = CompositionCalculator.Calculate(root, nodes);

        Assert.False(result.IsComplete);
        Assert.Equal("QuantityOverflow", Assert.Single(result.Diagnostics).Code);
        Assert.Null(Assert.Single(result.Items).Quantity);
        Assert.Null(result.TotalMassKg);
    }

    /// <summary>
    /// Проверяет, что переполнение суммы сгруппированного количества делает итог неизвестным.
    /// </summary>
    [Fact]
    public void GroupQuantityAdditionOverflowMakesGroupedQuantityUnknown()
    {
        var root = Guid.NewGuid();
        var nodes = new List<CompositionCalculationInput> { Node(root, [root], null, 1, PdmObjectType.Assembly) };
        var sharedPart = Guid.NewGuid();
        for (var branch = 0; branch < 2; branch++)
        {
            var branchId = Guid.NewGuid();
            var middleOne = Guid.NewGuid();
            var middleTwo = Guid.NewGuid();
            var branchPath = new[] { root, branchId };
            var middleOnePath = new[] { root, branchId, middleOne };
            var middleTwoPath = new[] { root, branchId, middleOne, middleTwo };
            var leafPath = new[] { root, branchId, middleOne, middleTwo, sharedPart };
            nodes.Add(Node(branchId, branchPath, [root], int.MaxValue, PdmObjectType.Assembly));
            nodes.Add(Node(middleOne, middleOnePath, branchPath, int.MaxValue, PdmObjectType.Assembly));
            nodes.Add(Node(middleTwo, middleTwoPath, middleOnePath, int.MaxValue, PdmObjectType.Assembly));
            nodes.Add(Node(sharedPart, leafPath, middleTwoPath, 6, PdmObjectType.Part, mass: 1m));
        }

        var result = CompositionCalculator.Calculate(root, nodes);

        Assert.False(result.IsComplete);
        Assert.Equal("QuantityOverflow", Assert.Single(result.Diagnostics).Code);
        Assert.Null(Assert.Single(result.Items).Quantity);
    }

    /// <summary>
    /// Проверяет, что переполнение массы строки оставляет массу неизвестной.
    /// </summary>
    [Fact]
    public void LineMassOverflowLeavesMassUnknown()
    {
        var root = Guid.NewGuid();
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).Prepend(root).ToArray();
        var nodes = new List<CompositionCalculationInput> { Node(root, [root], null, 1, PdmObjectType.Assembly) };
        for (var index = 1; index < ids.Length; index++)
        {
            var path = ids.Take(index + 1).ToArray();
            var parent = ids.Take(index).ToArray();
            nodes.Add(Node(ids[index], path, parent, index == ids.Length - 1 ? 6 : int.MaxValue,
                index == ids.Length - 1 ? PdmObjectType.Part : PdmObjectType.Assembly,
                mass: index == ids.Length - 1 ? 2m : null));
        }

        var result = CompositionCalculator.Calculate(root, nodes);

        Assert.False(result.IsComplete);
        Assert.Equal("MassOverflow", Assert.Single(result.Diagnostics).Code);
        Assert.Null(Assert.Single(result.Items).TotalMassKg);
        Assert.Null(result.TotalMassKg);
    }

    /// <summary>
    /// Проверяет блокировку итоговой массы при переполнении суммы масс.
    /// </summary>
    [Fact]
    public void TotalMassAdditionOverflowBlocksTheReportedTotal()
    {
        var root = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var result = CompositionCalculator.Calculate(root,
        [
            Node(root, [root], null, 1, PdmObjectType.Assembly),
            Node(first, [root, first], [root], 1, PdmObjectType.Part, mass: 6e28m),
            Node(second, [root, second], [root], 1, PdmObjectType.Part, mass: 6e28m)
        ]);

        Assert.False(result.IsComplete);
        Assert.Null(result.TotalMassKg);
        Assert.Equal("MassOverflow", Assert.Single(result.Diagnostics).Code);
        Assert.All(result.Items, item => Assert.NotNull(item.TotalMassKg));
    }

    /// <summary>
    /// Проверяет отклонение вхождения не корневого узла без родительского пути.
    /// </summary>
    [Fact]
    public void NonRootOccurrenceWithoutParentPathIsRejected()
    {
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => CompositionCalculator.Calculate(root,
        [
            Node(root, [root], null, 1, PdmObjectType.Assembly),
            Node(child, [root, child], null, 1, PdmObjectType.Part, mass: 1m)
        ]));
    }

    private static CompositionCalculationInput Node(Guid id, Guid[] path, Guid[]? parentPath,
        int localQuantity, PdmObjectType type, decimal? mass = 100m, Guid? versionId = null,
        bool hasVersion = true, bool isCycle = false) =>
        new(id, path, parentPath, localQuantity, type, type == PdmObjectType.StandardPart ? null : "D-1",
            type == PdmObjectType.StandardPart ? "Standard name" : "Part name", "Steel",
            hasVersion ? versionId ?? Guid.NewGuid() : null, 1, VersionState.Approved, mass, isCycle);
}
