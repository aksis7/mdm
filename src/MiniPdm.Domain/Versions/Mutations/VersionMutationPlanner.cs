using Resources = MiniPdm.Common.Resources;
using MiniPdm.Domain.Composition;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;

namespace MiniPdm.Domain.Versions.Mutations;

/// <summary>
/// Проверяет бизнес-условия изменения версии и строит план изменений без доступа к хранилищу.
/// </summary>
public static class VersionMutationPlanner
{
    /// <summary>
    ///     Планирует создание следующей версии «В работе» копированием выбранной версии.
    /// </summary>
    /// <param name="snapshot">
    ///     Текущее состояние объекта, выбранной версии и графа состава.
    /// </param>
    /// <returns>
    ///     План с новой версией либо причиной отказа.
    /// </returns>
    public static VersionMutationPlan Clone(VersionMutationSnapshot snapshot)
    {
        if (!IsConsistent(snapshot))
            return NotFound("VersionNotFound", Resources.BusinessLogicException.VersionNotFound);
        var source = snapshot.SelectedVersion;
        var max = snapshot.Object.Versions.Append(snapshot.Object.CurrentVersion).Where(x => x is not null)
            .Select(x => x!.Version).Append(source.Version).DefaultIfEmpty(0).Max();
        if (max == int.MaxValue)
            return Invalid("VersionNumberOverflow", Resources.BusinessLogicException.VersionOverflow);

        var clone = CopyVersion(snapshot.Object, source, max + 1, VersionState.InWork);
        var currentId = clone.Id;

        var graphError = ValidateFutureGraph(snapshot, currentId, clone);
        if (graphError is not null)
            return graphError;
        return Success(clone, currentId, clone, [], []);
    }

    /// <summary>
    ///     Планирует изменение атрибутов редактируемой версии без изменения идентичности объекта.
    /// </summary>
    /// <param name="snapshot">
    ///     Текущее состояние объекта и выбранной версии.
    /// </param>
    /// <param name="name">
    ///     Новое наименование версии либо null, если оно не задано.
    /// </param>
    /// <param name="material">
    ///     Новый материал детали либо null, если он не задан.
    /// </param>
    /// <param name="mass">
    ///     Новая масса одного изделия в килограммах либо null, если она не задана.
    /// </param>
    /// <returns>
    ///     План с изменённой версией либо причиной отказа.
    /// </returns>
    public static VersionMutationPlan UpdateAttributes(VersionMutationSnapshot snapshot, string? name,
        string? material, decimal? mass)
    {
        var precondition = Editable(snapshot);
        if (precondition is not null)
            return precondition;
        var obj = snapshot.Object;
        if (obj.Type == PdmObjectType.StandardPart)
        {
            if (name is not null && !string.Equals(name, obj.StandardName, StringComparison.Ordinal))
                return Invalid("IdentityChange", Resources.BusinessLogicException.StandardPartNameImmutable);
        }

        var validation = VersionAttributeRules.Validate(obj.Type,
            obj.Type == PdmObjectType.StandardPart ? obj.StandardName : name, material, mass);
        if (!validation.IsValid)
            return Invalid("InvalidAttributes", validation.Errors[0]);
        var changed = CopyVersion(obj, snapshot.SelectedVersion, snapshot.SelectedVersion.Version, snapshot.SelectedVersion.State, preserveId: true);
        changed.Name = obj.Type == PdmObjectType.StandardPart ? snapshot.SelectedVersion.Name : name;
        changed.Material = material;
        changed.Mass = mass;
        var currentId = obj.CurrentVersionId;
        var graphError = ValidateFutureGraph(snapshot, currentId, changed);
        if (graphError is not null)
            return graphError;
        snapshot.SelectedVersion.Name = changed.Name;
        snapshot.SelectedVersion.Material = changed.Material;
        snapshot.SelectedVersion.Mass = changed.Mass;
        return Success(snapshot.SelectedVersion, currentId, null, [], validation.Warnings);
    }

    /// <summary>
    ///     Планирует полную замену строк состава редактируемой версии сборки.
    /// </summary>
    /// <param name="snapshot">
    ///     Текущее состояние объекта, выбранной версии и графа состава.
    /// </param>
    /// <param name="composition">
    ///     Запрошенные дочерние объекты и их количества.
    /// </param>
    /// <returns>
    ///     План с изменениями, предупреждениями либо причиной отказа.
    /// </returns>
    public static VersionMutationPlan ReplaceComposition(VersionMutationSnapshot snapshot,
        IReadOnlyList<CompositionItem> composition)
    {
        var precondition = Editable(snapshot);
        if (precondition is not null)
            return precondition;
        if (snapshot.Object.Type != PdmObjectType.Assembly)
            return Invalid("CompositionRequiresAssembly", Resources.InputLogicException.CompositionMustBeAssembly);

        var normalized = CompositionRules.Normalize(composition.Select(x => (x.ChildObjectId, x.Quantity)));
        if (normalized.Errors.Count != 0)
            return Invalid("InvalidComposition", normalized.Errors[0]);
        foreach (var childId in normalized.Items.Keys)
            if (!snapshot.ExistingChildIds.Contains(childId))
                return Invalid("UnknownChild", string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.BusinessLogicException.UnknownChildObject, childId));

        var changed = CopyVersion(snapshot.Object, snapshot.SelectedVersion, snapshot.SelectedVersion.Version,
            snapshot.SelectedVersion.State, preserveId: true);
        changed.Components.Clear();
        foreach (var (childId, quantity) in normalized.Items)
            changed.Components.Add(new BomLink { ParentVersionId = changed.Id, ChildObjectId = childId, Quantity = quantity });

        var currentId = snapshot.Object.CurrentVersionId;
        var graphError = ValidateFutureGraph(snapshot, currentId, changed);
        if (graphError is not null)
            return graphError;
        var selected = snapshot.SelectedVersion;
        var removed = selected.Components.Where(x => !normalized.Items.ContainsKey(x.ChildObjectId)).ToArray();
        foreach (var link in removed)
            selected.Components.Remove(link);
        foreach (var (childId, quantity) in normalized.Items)
        {
            var existing = selected.Components.SingleOrDefault(x => x.ChildObjectId == childId);
            if (existing is null)
                selected.Components.Add(new BomLink { ParentVersionId = selected.Id, ChildObjectId = childId, Quantity = quantity });
            else
                existing.Quantity = quantity;
        }
        return Success(selected, currentId, null, removed, normalized.Warnings);
    }

    /// <summary>
    ///     Проверяет и планирует перевод версии в новое состояние с учётом текущего указателя и графа состава.
    /// </summary>
    /// <param name="snapshot">
    ///     Текущее состояние объекта, выбранной версии и графа состава.
    /// </param>
    /// <param name="newState">
    ///     Запрошенное новое состояние версии.
    /// </param>
    /// <returns>
    ///     План изменения состояния либо причину отказа.
    /// </returns>
    public static VersionMutationPlan ChangeState(VersionMutationSnapshot snapshot, VersionState newState)
    {
        if (!IsConsistent(snapshot))
            return NotFound("VersionNotFound", Resources.BusinessLogicException.VersionNotFound);
        var version = snapshot.SelectedVersion;
        if (version.State == newState)
            return Conflict("StateUnchanged", Resources.BusinessLogicException.VersionStateUnchanged);
        if (!Enum.IsDefined(newState) || !ObjectVersion.CanTransition(version.State, newState))
            return Conflict("InvalidStateTransition", Resources.BusinessLogicException.InvalidVersionStateTransition);

        var changed = CopyVersion(snapshot.Object, version, version.Version, newState, preserveId: true);
        var currentId = snapshot.Object.CurrentVersionId;
        if (newState == VersionState.Cancelled && currentId == version.Id)
            currentId = snapshot.Object.Versions.Append(version)
                .Where(x => x.Id != version.Id && x.State != VersionState.Cancelled)
                .OrderByDescending(x => x.Version).Select(x => (Guid?)x.Id).FirstOrDefault();

        var graphError = ValidateFutureGraph(snapshot, currentId, changed);
        if (graphError is not null)
            return graphError;
        version.State = newState;
        return Success(version, currentId, null, [], []);
    }

    private static VersionMutationPlan? Editable(VersionMutationSnapshot snapshot)
    {
        if (!IsConsistent(snapshot))
            return NotFound("VersionNotFound", Resources.BusinessLogicException.VersionNotFound);
        if (snapshot.SelectedVersion.State != VersionState.InWork)
            return Conflict("VersionImmutable", Resources.BusinessLogicException.VersionImmutable);
        return null;
    }

    private static bool IsConsistent(VersionMutationSnapshot snapshot) =>
        snapshot.SelectedVersion.ObjectId == snapshot.Object.Id &&
        (snapshot.Object.Versions.Any(x => x.Id == snapshot.SelectedVersion.Id) ||
         snapshot.Object.CurrentVersionId == snapshot.SelectedVersion.Id);

    private static VersionMutationPlan? ValidateFutureGraph(VersionMutationSnapshot snapshot, Guid? currentId,
        ObjectVersion candidate)
    {
        var edges = snapshot.CurrentGraph.Where(x => x.ParentId != snapshot.Object.Id).ToList();
        if (snapshot.Object.Type == PdmObjectType.Assembly && currentId is not null)
        {
            var currentVersion = currentId == candidate.Id ? candidate :
                snapshot.Object.Versions.FirstOrDefault(x => x.Id == currentId) ??
                (snapshot.Object.CurrentVersion?.Id == currentId ? snapshot.Object.CurrentVersion : null);
            if (currentVersion is not null)
                edges.AddRange(currentVersion.Components.Select(x => new CompositionGraphEdge(snapshot.Object.Id, x.ChildObjectId)));
        }
        var cycle = CompositionGraph.FindCyclePath(edges);
        return cycle is null ? null : new VersionMutationPlan(VersionMutationStatus.Conflict, null, null, null, [],
            new VersionMutationError("Cycle", Resources.BusinessLogicException.CompositionCycle, cycle), []);
    }

    private static ObjectVersion CopyVersion(PdmObject obj, ObjectVersion source, int number, VersionState state, bool preserveId = false)
    {
        var copy = new ObjectVersion
        {
            Id = preserveId ? source.Id : Guid.NewGuid(),
            ObjectId = obj.Id,
            Object = obj,
            Version = number,
            State = state,
            Name = source.Name,
            Material = source.Material,
            Mass = source.Mass,
            SourceReference = source.SourceReference
        };
        foreach (var link in source.Components)
            copy.Components.Add(new BomLink { ParentVersionId = copy.Id, ChildObjectId = link.ChildObjectId, Quantity = link.Quantity });
        return copy;
    }

    private static VersionMutationPlan Success(ObjectVersion version, Guid? currentId, ObjectVersion? newVersion,
        IReadOnlyList<BomLink> removed, IReadOnlyList<string> warnings) =>
        new(VersionMutationStatus.Succeeded, version, currentId, newVersion, removed, null, warnings);
    private static VersionMutationPlan Invalid(string code, string message) =>
        new(VersionMutationStatus.Invalid, null, null, null, [], new VersionMutationError(code, message), []);
    private static VersionMutationPlan Conflict(string code, string message) =>
        new(VersionMutationStatus.Conflict, null, null, null, [], new VersionMutationError(code, message), []);
    private static VersionMutationPlan NotFound(string code, string message) =>
        new(VersionMutationStatus.NotFound, null, null, null, [], new VersionMutationError(code, message), []);
}
