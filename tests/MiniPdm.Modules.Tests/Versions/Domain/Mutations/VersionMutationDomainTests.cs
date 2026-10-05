using MiniPdm.Domain.Composition;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Domain.Versions.Mutations;
using Xunit;

namespace MiniPdm.Modules.Tests.Versions.Domain.Mutations;

/// <summary>
/// Проверяет доменные правила клонирования, состояний, атрибутов и изменения состава версий.
/// </summary>
public sealed class VersionMutationDomainTests
{
    /// <summary>
    /// Проверяет копирование данных версии и назначение номера выше максимального исторического.
    /// </summary>
    [Fact]
    public void CloneCopiesDataAndUsesMaximumHistoricalNumberPlusOne()
    {
        var fixture = Create(PdmObjectType.Assembly);
        fixture.Object.Versions.Add(Version(fixture.Object, 3, VersionState.Cancelled));
        fixture.Selected.Components.Add(new BomLink { ParentVersionId = fixture.Selected.Id, ChildObjectId = fixture.ChildId, Quantity = 2 });
        fixture.Selected.SourceReference = "source/path";

        var plan = VersionMutationPlanner.Clone(fixture.Snapshot);

        Assert.Equal(VersionMutationStatus.Succeeded, plan.Status);
        Assert.Equal(4, plan.NewVersion!.Version);
        Assert.Equal(VersionState.InWork, plan.NewVersion.State);
        Assert.Equal(fixture.Selected.Name, plan.NewVersion.Name);
        Assert.Equal("source/path", plan.NewVersion.SourceReference);
        Assert.Equal(plan.NewVersion.Id, plan.NewVersion.Components.Single().ParentVersionId);
        Assert.Equal(plan.NewVersion.Id, plan.DesiredCurrentVersionId);
    }

    /// <summary>
    /// Проверяет запрет редактирования неизменяемых состояний версии.
    /// </summary>
    /// <param name="state">Состояние версии.</param>
    [Theory]
    [InlineData(VersionState.Approved)]
    [InlineData(VersionState.Cancelled)]
    public void ImmutableVersionsCannotBeEdited(VersionState state)
    {
        var fixture = Create(PdmObjectType.Part, state);
        var plan = VersionMutationPlanner.UpdateAttributes(fixture.Snapshot, "Changed", "Steel", 1m);
        Assert.Equal(VersionMutationStatus.Conflict, plan.Status);
        Assert.Equal("VersionImmutable", plan.Error!.Code);
    }

    /// <summary>
    /// Проверяет переключение на старшую действующую историческую версию после отмены текущей.
    /// </summary>
    [Fact]
    public void CancellingCurrentVersionFallsBackToHighestActiveHistoricalVersion()
    {
        var fixture = Create(PdmObjectType.Part);
        var prior = Version(fixture.Object, 1, VersionState.Approved);
        fixture.Object.Versions.Add(prior);
        fixture.Object.CurrentVersion = fixture.Selected;
        fixture.Object.CurrentVersionId = fixture.Selected.Id;
        var plan = VersionMutationPlanner.ChangeState(fixture.Snapshot, VersionState.Cancelled);
        Assert.Equal(VersionMutationStatus.Succeeded, plan.Status);
        Assert.Equal(prior.Id, plan.DesiredCurrentVersionId);
        Assert.Equal(VersionState.Cancelled, plan.Version!.State);
    }

    /// <summary>
    /// Проверяет отклонение смены состояния, если резервный действующий граф образует цикл.
    /// </summary>
    [Fact]
    public void StateChangeThatMakesFallbackGraphCyclicConflictsWithClosedPath()
    {
        var fixture = Create(PdmObjectType.Assembly);
        var fallback = Version(fixture.Object, 1, VersionState.Approved);
        fallback.Components.Add(new BomLink { ParentVersionId = fallback.Id, ChildObjectId = fixture.ChildId, Quantity = 1 });
        fixture.Object.Versions.Add(fallback);
        fixture.Object.CurrentVersion = fixture.Selected;
        fixture.Object.CurrentVersionId = fixture.Selected.Id;
        var graph = new[] { new CompositionGraphEdge(fixture.ChildId, fixture.Object.Id) };
        var snapshot = fixture.Snapshot with
        {
            CurrentGraph = graph
        };

        var plan = VersionMutationPlanner.ChangeState(snapshot, VersionState.Cancelled);

        Assert.Equal(VersionMutationStatus.Conflict, plan.Status);
        Assert.Equal("Cycle", plan.Error!.Code);
        Assert.NotNull(plan.Error.CyclePath);
        Assert.Equal(plan.Error.CyclePath[0], plan.Error.CyclePath[^1]);
        Assert.Equal(new[] { fixture.Object.Id, fixture.ChildId }.OrderBy(x => x),
            plan.Error.CyclePath[..^1].OrderBy(x => x));
    }

    /// <summary>
    /// Проверяет, что изменение исторического состава версии в работе не меняет указатель текущей версии.
    /// </summary>
    [Fact]
    public void HistoricalInWorkCompositionEditDoesNotChangeCurrentPointer()
    {
        var fixture = Create(PdmObjectType.Assembly);
        var current = Version(fixture.Object, 2, VersionState.Approved);
        fixture.Object.Versions.Add(current);
        fixture.Object.CurrentVersion = current;
        fixture.Object.CurrentVersionId = current.Id;
        var plan = VersionMutationPlanner.ReplaceComposition(fixture.Snapshot, [new(fixture.ChildId, 3)]);
        Assert.Equal(VersionMutationStatus.Succeeded, plan.Status);
        Assert.Equal(current.Id, plan.DesiredCurrentVersionId);
        Assert.Equal(fixture.Selected.Id, plan.Version!.Id);
        Assert.Equal(3, plan.Version.Components.Single().Quantity);
    }

    /// <summary>
    /// Проверяет добавление дочернего объекта без текущей версии и обновление сохранённых связей на месте.
    /// </summary>
    [Fact]
    public void ExistingChildWithoutCurrentVersionCanBeAddedAndRetainedLinksUpdateInPlace()
    {
        var fixture = Create(PdmObjectType.Assembly);
        fixture.Object.CurrentVersion = null;
        fixture.Object.CurrentVersionId = null;
        var prior = new BomLink { ParentVersionId = fixture.Selected.Id, ChildObjectId = fixture.ChildId, Quantity = 1 };
        fixture.Selected.Components.Add(prior);

        var plan = VersionMutationPlanner.ReplaceComposition(fixture.Snapshot, [new(fixture.ChildId, 5)]);

        Assert.Equal(VersionMutationStatus.Succeeded, plan.Status);
        Assert.Null(plan.DesiredCurrentVersionId);
        Assert.Same(prior, fixture.Selected.Components.Single());
        Assert.Equal(5, prior.Quantity);
        Assert.Empty(plan.RemovedLinks);
    }

    /// <summary>
    /// Проверяет отклонение циклического состава без изменения выбранной версии.
    /// </summary>
    [Fact]
    public void CompositionCycleFailsWithoutChangingSelectedVersion()
    {
        var fixture = Create(PdmObjectType.Assembly);
        var snapshot = fixture.Snapshot with
        {
            CurrentGraph = [new CompositionGraphEdge(fixture.ChildId, fixture.Object.Id)]
        };

        var plan = VersionMutationPlanner.ReplaceComposition(snapshot, [new(fixture.ChildId, 1)]);

        Assert.Equal(VersionMutationStatus.Conflict, plan.Status);
        Assert.Equal("Cycle", plan.Error!.Code);
        Assert.Empty(fixture.Selected.Components);
    }

    /// <summary>
    /// Проверяет нормализацию корректных повторов состава и отклонение ошибочных строк и неизвестных дочерних объектов.
    /// </summary>
    [Fact]
    public void CompositionNormalizesValidDuplicatesAndRejectsInvalidRowsAndUnknownChildren()
    {
        var fixture = Create(PdmObjectType.Assembly);
        var duplicate = VersionMutationPlanner.ReplaceComposition(fixture.Snapshot,
            [new(fixture.ChildId, 2), new(fixture.ChildId, 4)]);
        Assert.Equal(VersionMutationStatus.Succeeded, duplicate.Status);
        Assert.Equal(6, duplicate.Version!.Components.Single().Quantity);
        Assert.Single(duplicate.Warnings);

        var badRow = VersionMutationPlanner.ReplaceComposition(fixture.Snapshot,
            [new(fixture.ChildId, 0), new(fixture.ChildId, 4)]);
        Assert.Equal(VersionMutationStatus.Invalid, badRow.Status);

        var unknown = VersionMutationPlanner.ReplaceComposition(fixture.Snapshot, [new(Guid.NewGuid(), 1)]);
        Assert.Equal("UnknownChild", unknown.Error!.Code);
    }

    /// <summary>
    /// Проверяет отклонение переполнения номера при клонировании и атрибутов неподходящего типа.
    /// </summary>
    [Fact]
    public void CloneOverflowAndAttributeTypeRulesAreInvalid()
    {
        var fixture = Create(PdmObjectType.Part);
        fixture.Object.Versions.Add(Version(fixture.Object, int.MaxValue, VersionState.Cancelled));
        Assert.Equal("VersionNumberOverflow", VersionMutationPlanner.Clone(fixture.Snapshot).Error!.Code);

        var part = Create(PdmObjectType.Part);
        Assert.Equal(VersionMutationStatus.Invalid,
            VersionMutationPlanner.UpdateAttributes(part.Snapshot, null, "", null).Status);
        var standard = Create(PdmObjectType.StandardPart);
        Assert.Equal(VersionMutationStatus.Invalid,
            VersionMutationPlanner.UpdateAttributes(standard.Snapshot, "Different", null, 1m).Status);
        Assert.Equal(VersionMutationStatus.Invalid,
            VersionMutationPlanner.UpdateAttributes(standard.Snapshot, null, null, null).Status);
        var assembly = Create(PdmObjectType.Assembly);
        Assert.Equal(VersionMutationStatus.Succeeded,
            VersionMutationPlanner.UpdateAttributes(assembly.Snapshot, "New name", null, null).Status);
    }

    /// <summary>
    /// Проверяет отклонение недопустимых типизированных атрибутов без смены выбранной версии.
    /// </summary>
    [Fact]
    public void TypeSpecificInvalidAttributesFailWithoutChangingSelectedVersion()
    {
        var assembly = Create(PdmObjectType.Assembly);
        var assemblyPlan = VersionMutationPlanner.UpdateAttributes(assembly.Snapshot, "Renamed", "Steel", 2m);
        Assert.Equal(VersionMutationStatus.Invalid, assemblyPlan.Status);
        Assert.Equal("Name", assembly.Selected.Name);
        Assert.Null(assembly.Selected.Material);
        Assert.Null(assembly.Selected.Mass);

        var standard = Create(PdmObjectType.StandardPart);
        var standardPlan = VersionMutationPlanner.UpdateAttributes(standard.Snapshot, "Bolt", "Steel", 2m);
        Assert.Equal(VersionMutationStatus.Invalid, standardPlan.Status);
        Assert.Null(standard.Selected.Name);
        Assert.Null(standard.Selected.Material);
        Assert.Equal(1m, standard.Selected.Mass);

        var accepted = VersionMutationPlanner.UpdateAttributes(standard.Snapshot, "Bolt", null, 2m);
        Assert.Equal(VersionMutationStatus.Succeeded, accepted.Status);
        Assert.Null(standard.Selected.Name);
        Assert.Equal(2m, standard.Selected.Mass);
    }

    private static Fixture Create(PdmObjectType type, VersionState state = VersionState.InWork)
    {
        var obj = new PdmObject { Type = type, StandardName = type == PdmObjectType.StandardPart ? "Bolt" : null };
        var version = new ObjectVersion
        {
            ObjectId = obj.Id,
            Object = obj,
            Version = 1,
            State = state,
            Name = type == PdmObjectType.StandardPart ? null : "Name",
            Material = type == PdmObjectType.Part ? "Steel" : null,
            Mass = type == PdmObjectType.Assembly ? null : 1m
        };
        obj.Versions.Add(version);
        obj.CurrentVersion = version;
        obj.CurrentVersionId = version.Id;
        var child = Guid.NewGuid();
        return new Fixture(obj, version, child,
            new VersionMutationSnapshot(obj, version, [], new HashSet<Guid> { child }));
    }

    private static ObjectVersion Version(PdmObject obj, int number, VersionState state) =>
        new()
        {
            ObjectId = obj.Id,
            Object = obj,
            Version = number,
            State = state,
            Name = "Old"
        };

    /// <summary>
    /// Содержит объект, выбранную версию и снимок для теста изменения версии.
    /// </summary>
    private sealed record Fixture(PdmObject Object, ObjectVersion Selected, Guid ChildId, VersionMutationSnapshot Snapshot)
    {
        /// <summary>
        /// Объект, используемый тестовым сценарием.
        /// </summary>
        public PdmObject Object { get; init; } = Object;

        /// <summary>
        /// Версия объекта, выбранная для изменения.
        /// </summary>
        public ObjectVersion Selected { get; init; } = Selected;

        /// <summary>
        /// Идентификатор дочернего объекта в снимке.
        /// </summary>
        public Guid ChildId { get; init; } = ChildId;

        /// <summary>
        /// Снимок состояния для проверки операции изменения версии.
        /// </summary>
        public VersionMutationSnapshot Snapshot { get; init; } = Snapshot;
    }
}
