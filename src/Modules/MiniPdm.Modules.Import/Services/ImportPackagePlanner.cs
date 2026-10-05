using MiniPdm.Common.Exceptions;
using Resources = MiniPdm.Common.Resources;
using System.Text.Json;
using MiniPdm.Contracts.Modules.Import.DtoModels;
using MiniPdm.Domain.Composition;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Modules.Import.Abstractions.Cad;
using MiniPdm.Modules.Import.DtoModels.Cad;
using MiniPdm.Modules.Import.Abstractions;
using MiniPdm.Modules.Import.Abstractions.Database;
using MiniPdm.Modules.Import.DtoModels.Database;

namespace MiniPdm.Modules.Import.Services;

/// <summary>
/// Хранит подготовленные изменения импорта до преобразования в транзакционный план записи.
/// </summary>
/// <param name="validator">Результаты проверки входных файлов.</param>
/// <param name="newObjects">Создаваемые PDM-объекты.</param>
/// <param name="newVersions">Создаваемые версии.</param>
/// <param name="currentVersions">Новые назначения текущих версий.</param>
/// <param name="targets">Связь файлов с целевыми объектами.</param>
/// <param name="removedLinks">Удаляемые связи состава.</param>
internal sealed class ImportPackagePlan(ImportPackageValidator validator, IReadOnlyList<PdmObject> newObjects,
    IReadOnlyList<ObjectVersion> newVersions, IReadOnlyList<CurrentVersionAssignmentDto> currentVersions,
    IReadOnlyDictionary<ImportPackageValidator.FileEntry, PdmObject> targets, IReadOnlyList<BomLink> removedLinks)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    /// <summary>
    /// Строки отчёта по всем файлам пакета.
    /// </summary>
    public IReadOnlyList<ImportFileResultDto> Files => validator.Files.Select(x => x.ToDto()).ToArray();

    /// <summary>
    /// Добавляет ссылки на сохранённые файлы и формирует план записи с сериализованным отчётом.
    /// </summary>
    /// <param name="importId">Идентификатор операции импорта.</param>
    /// <param name="sourceStorage">Хранилище для построения ссылок принятых файлов.</param>
    /// <returns>Полный план изменений для транзакции базы данных.</returns>
    public ImportWritePlanDto ToWritePlan(Guid importId, IImportSourceStorage sourceStorage)
    {
        foreach (var entry in validator.Files.Where(x => x.Accepted && x.Action is not ImportFileAction.Unchanged))
        {
            var objectId = targets[entry].Id;
            var version = newVersions.SingleOrDefault(x => x.ObjectId == objectId)
                ?? currentVersions.SingleOrDefault(x => x.Object.Id == objectId)?.Version
                ?? targets[entry].CurrentVersion;
            if (version is not null)
                version.SourceReference = sourceStorage.GetSourceReference(importId, entry.FileName);
        }
        var report = new ImportReportDto
        {
            ImportId = importId,
            Files = Files
        };
        return new ImportWritePlanDto
        {
            NewObjects = newObjects,
            NewVersions = newVersions,
            CurrentVersions = currentVersions,
            ReportJson = JsonSerializer.Serialize(report, JsonOptions),
            RemovedLinks = removedLinks
        };
    }
}

internal static class ImportPackagePlanner
{
    /// <summary>
    /// Строит изменения объектов и версий по проверенному пакету и снимку базы данных.
    /// </summary>
    /// <param name="validator">Проверенные файлы и их связи.</param>
    /// <param name="snapshot">Найденные объекты и текущий граф состава.</param>
    /// <returns>План создания, обновления и назначения версий.</returns>
    public static ImportPackagePlan Prepare(ImportPackageValidator validator, ImportSnapshotDto snapshot)
    {
        var files = validator.Files;
        var byFile = validator.ByFile;
        var targetObjects = new Dictionary<ImportPackageValidator.FileEntry, PdmObject>();
        var createObjects = new Dictionary<ImportPackageValidator.FileEntry, PdmObject>();

        // Allocate stable identities up front; they are not attached to the session until validation stabilizes.
        foreach (var file in files.Where(x => x.Accepted))
        {
            var doc = file.Document!;
            var sameIdentity = snapshot.ExistingObjects.Where(x => doc.Type == PdmObjectType.StandardPart
                ? x.Type == PdmObjectType.StandardPart && x.NormalizedName == ObjectIdentity.NormalizeStandardName(doc.Name)
                : x.Designation == doc.Designation).ToArray();
            var otherType = snapshot.ExistingObjects.Any(x => doc.Type != PdmObjectType.StandardPart
                ? x.Designation == doc.Designation && x.Type != doc.Type
                : x.Type == PdmObjectType.StandardPart && x.NormalizedName == ObjectIdentity.NormalizeStandardName(doc.Name)
                    && x.Type != doc.Type);
            if (otherType)
            {
                Reject(file, Resources.BusinessLogicException.IdentityTypeConflict);
                continue;
            }
            var existing = sameIdentity.SingleOrDefault();
            if (existing is null)
            {
                var pdmObject = new PdmObject
                {
                    Type = doc.Type,
                    Designation = doc.Designation,
                    StandardName = doc.Type == PdmObjectType.StandardPart ? doc.Name : null,
                    NormalizedName = doc.Type == PdmObjectType.StandardPart ? ObjectIdentity.NormalizeStandardName(doc.Name) : null,
                    Versions = new List<ObjectVersion>()
                };
                createObjects.Add(file, pdmObject);
                targetObjects.Add(file, pdmObject);
                file.Action = ImportFileAction.Created;
            }
            else
            {
                targetObjects.Add(file, existing);
                file.Action = ImportFileAction.Unchanged;
            }
        }

        CascadePackageRejections(files, byFile);
        while (true)
        {
            var compositions = ResolveCompositions(files, byFile, targetObjects);
            CascadePackageRejections(files, byFile);
            compositions = ResolveCompositions(files, byFile, targetObjects);
            var graph = snapshot.CurrentGraph.Select(x => new CompositionGraphEdge(x.ParentId, x.ChildId)).ToHashSet();
            foreach (var pair in compositions)
            {
                graph.RemoveWhere(x => x.ParentId == pair.Key.Id);
                foreach (var child in pair.Value.Keys)
                    graph.Add(new CompositionGraphEdge(pair.Key.Id, child));
            }
            var cycleNodes = CompositionGraph.FindCycleNodes(graph);
            if (cycleNodes.Count == 0)
                break;
            var candidates = files.Where(x => x.Accepted && x.Document?.Type == PdmObjectType.Assembly
                    && targetObjects.TryGetValue(x, out var obj) && cycleNodes.Contains(obj.Id)).ToArray();
            if (candidates.Length == 0)
                throw new BusinessLogicException(Resources.BusinessLogicException.CompositionCycleInDatabase);
            foreach (var file in candidates)
                Reject(file, Resources.BusinessLogicException.CompositionCycle);
            CascadePackageRejections(files, byFile);
        }

        var finalCompositions = ResolveCompositions(files, byFile, targetObjects);
        var newObjects = new List<PdmObject>();
        var newVersions = new List<ObjectVersion>();
        var currentAssignments = new List<CurrentVersionAssignmentDto> { };
        var removedLinks = new List<BomLink>();
        foreach (var file in files.Where(x => x.Accepted))
        {
            var doc = file.Document!;
            var target = targetObjects[file];
            finalCompositions.TryGetValue(target, out var desiredComposition);
            desiredComposition ??= new Dictionary<Guid, int>();
            var isNew = createObjects.ContainsKey(file);
            if (isNew)
            {
                target.Versions.Clear();
                var version = NewVersion(target, doc, desiredComposition, 1);
                newObjects.Add(target);
                newVersions.Add(version);
                currentAssignments.Add(new CurrentVersionAssignmentDto
                {
                    Object = target,
                    Version = version
                });
                file.Action = ImportFileAction.Created;
                continue;
            }

            var versions = target.Versions.ToArray();
            var current = target.CurrentVersion;
            var changed = current is null || AttributesChanged(target, current, doc) ||
                !CompositionEquals(current.Components, desiredComposition);
            if (!changed)
            {
                file.Action = ImportFileAction.Unchanged;
                continue;
            }

            if (current is { State: VersionState.InWork })
            {
                current.Name = target.Type == PdmObjectType.StandardPart ? current.Name : doc.Name;
                current.Material = doc.Material;
                current.Mass = doc.Mass;
                ReplaceComposition(current, desiredComposition, removedLinks);
                file.Action = ImportFileAction.Updated;
            }
            else
            {
                var nextNumber = versions.Length == 0 ? 1 : checked(versions.Max(x => x.Version) + 1);
                var version = NewVersion(target, doc, desiredComposition, nextNumber);
                newVersions.Add(version);
                currentAssignments.Add(new CurrentVersionAssignmentDto
                {
                    Object = target,
                    Version = version
                });
                file.Action = ImportFileAction.NewVersion;
            }
        }

        return new ImportPackagePlan(validator, newObjects, newVersions, currentAssignments, targetObjects, removedLinks);
    }

    private static Dictionary<PdmObject, Dictionary<Guid, int>> ResolveCompositions(
        IReadOnlyList<ImportPackageValidator.FileEntry> files,
        IReadOnlyDictionary<string, ImportPackageValidator.FileEntry> byFile,
        IReadOnlyDictionary<ImportPackageValidator.FileEntry, PdmObject> targetObjects)
    {
        var result = new Dictionary<PdmObject, Dictionary<Guid, int>>();
        foreach (var file in files.Where(x => x.Accepted && x.Document?.Type == PdmObjectType.Assembly))
        {
            var composition = new Dictionary<Guid, int>();
            foreach (var (fileName, count) in file.ComponentCounts)
            {
                if (!byFile.TryGetValue(fileName, out var child) || !child.Accepted || !targetObjects.TryGetValue(child, out var childObject))
                {
                    Reject(file, string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.BusinessLogicException.ImportComponentUnavailable, fileName));
                    break;
                }
                composition[childObject.Id] = count;
            }
            if (file.Accepted)
                result[targetObjects[file]] = composition;
        }
        return result;
    }

    private static void CascadePackageRejections(IReadOnlyList<ImportPackageValidator.FileEntry> files,
        IReadOnlyDictionary<string, ImportPackageValidator.FileEntry> byFile)
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var file in files.Where(x => x.Accepted && x.Document?.Type == PdmObjectType.Assembly))
                foreach (var component in file.ComponentCounts.Keys)
                    if (!byFile.TryGetValue(component, out var child) || !child.Accepted)
                    {
                        Reject(file, string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.BusinessLogicException.ImportComponentRejected, component));
                        changed = true;
                        break;
                    }
        } while (changed);
    }

    private static bool AttributesChanged(PdmObject obj, ObjectVersion version, CadDocumentDto doc) =>
        (obj.Type == PdmObjectType.StandardPart ? false : version.Name != doc.Name) ||
        version.Material != doc.Material || version.Mass != doc.Mass;

    private static bool CompositionEquals(ICollection<BomLink> existing, Dictionary<Guid, int> desired) =>
        existing.Count == desired.Count && existing.All(x => desired.TryGetValue(x.ChildObjectId, out var quantity) && quantity == x.Quantity);

    private static void ReplaceComposition(ObjectVersion version, Dictionary<Guid, int> desired, List<BomLink> removedLinks)
    {
        foreach (var link in version.Components.Where(x => !desired.ContainsKey(x.ChildObjectId)).ToArray())
        {
            version.Components.Remove(link);
            removedLinks.Add(link);
        }
        foreach (var pair in desired)
        {
            var link = version.Components.SingleOrDefault(x => x.ChildObjectId == pair.Key);
            if (link is null)
                version.Components.Add(new BomLink { ParentVersionId = version.Id, ChildObjectId = pair.Key, Quantity = pair.Value });
            else
                link.Quantity = pair.Value;
        }
    }

    private static ObjectVersion NewVersion(PdmObject obj, CadDocumentDto doc, Dictionary<Guid, int> composition, int number)
    {
        var version = new ObjectVersion
        {
            ObjectId = obj.Id,
            Object = null,
            Version = number,
            State = VersionState.InWork,
            Name = obj.Type == PdmObjectType.StandardPart ? null : doc.Name,
            Material = doc.Material,
            Mass = doc.Mass
        };
        foreach (var pair in composition)
            version.Components.Add(new BomLink { ParentVersionId = version.Id, ChildObjectId = pair.Key, Quantity = pair.Value });
        return version;
    }

    private static void Reject(ImportPackageValidator.FileEntry file, string reason)
    {
        file.Reason ??= reason;
        file.Action = null;
    }
}
