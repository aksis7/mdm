using MiniPdm.Contracts.Modules.Import.DtoModels;
using MiniPdm.Domain.Composition;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Modules.Import.Abstractions;
using MiniPdm.Modules.Import.Abstractions.Cad;
using MiniPdm.Modules.Import.DtoModels.Cad;
using MiniPdm.Modules.Import.Services;
using MiniPdm.Modules.Import.Abstractions.Database;
using MiniPdm.Modules.Import.DtoModels.Database;
using Xunit;

namespace MiniPdm.Modules.Tests.Import.Services;

/// <summary>
/// Проверяет бизнес-правила импорта документов, версий, состава и отката файловых изменений.
/// </summary>
public sealed class ImportServiceTests
{
    /// <summary>
    /// Проверяет, что дублирующиеся идентичности отклоняются все вместе, а родительские документы отклоняются каскадно.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Duplicate_identities_are_all_rejected_and_parents_are_cascaded()
    {
        var part1 = Doc("one.m3d", PdmObjectType.Part, "АБВГ.301245.001", mass: 1m);
        var part2 = Doc("two.m3d", PdmObjectType.Part, "АБВГ.301245.001", mass: 1m);
        var assembly = Doc("top.a3d", PdmObjectType.Assembly, "АБВГ.301245.002",
            material: null, components: [new CadComponentDto
            {
                                File = "one.m3d",
                                Count = 1
            }]);
        var (service, _) = Make([part1, part2, assembly], []);

        var report = await service.ExecuteAsync(Guid.NewGuid(), Source, default);

        Assert.Equal(0, report.AcceptedCount);
        Assert.Equal(3, report.RejectedCount);
        Assert.Contains("identity", report.Files.Single(x => x.FileName == "one.m3d").Reason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rejected", report.Files.Single(x => x.FileName == "top.a3d").Reason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Проверяет, что документ детали без массы принимается с предупреждением.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Missing_part_mass_is_accepted_with_warning()
    {
        var (service, _) = Make([Doc("part.m3d", PdmObjectType.Part, "АБВГ.301245.001")], []);

        var report = await service.ExecuteAsync(Guid.NewGuid(), Source, default);

        var file = Assert.Single(report.Files);
        Assert.Equal(ImportFileStatus.Accepted, file.Status);
        Assert.Contains("Part mass is missing.", file.Warnings);
        Assert.Equal(1, report.WarningCount);
    }

    /// <summary>
    /// Проверяет, что совпадающие нормализованные стандартные наименования отклоняются даже при разной массе.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Normalized_standard_name_duplicates_are_rejected_even_when_masses_differ()
    {
        var first = Doc("first.m3d", PdmObjectType.StandardPart, null, " M8   bolt ", material: null, mass: 1m);
        var second = Doc("second.m3d", PdmObjectType.StandardPart, null, "m8 bolt", material: null, mass: 12m);
        var (service, _) = Make([first, second], []);

        var report = await service.ExecuteAsync(Guid.NewGuid(), Source, default);

        Assert.Equal(2, report.RejectedCount);
        Assert.All(report.Files, file => Assert.Equal(ImportFileStatus.Rejected, file.Status));
    }

    /// <summary>
    /// Проверяет, что документ с недопустимым количеством не позволяет принять документ с дублирующей идентичностью.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Invalid_quantity_document_still_rejects_valid_duplicate_identity()
    {
        var invalidAssembly = Doc("invalid.a3d", PdmObjectType.Assembly, "АБВГ.301245.010", material: null,
            components: [new CadComponentDto
            {
                                File = "child.m3d",
                                Count = 0
            }]);
        var validPart = Doc("valid.m3d", PdmObjectType.Part, "АБВГ.301245.010", "Same designation", "Steel", 1m);
        var (service, _) = Make([invalidAssembly, validPart], []);

        var report = await service.ExecuteAsync(Guid.NewGuid(), Source, default);

        Assert.Equal(2, report.RejectedCount);
        Assert.Contains("positive", report.Files.Single(x => x.FileName == "invalid.a3d").Reason!);
        Assert.Contains("identity", report.Files.Single(x => x.FileName == "valid.m3d").Reason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Проверяет, что ошибка чтения частичного документа не отменяет отклонение дублирующей идентичности.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Partial_document_with_reader_error_still_rejects_valid_duplicate_identity()
    {
        var partial = Doc("partial.m3d", PdmObjectType.Part, "АБВГ.301245.020", "Partial", "Steel", 1m);
        var valid = Doc("valid.m3d", PdmObjectType.Part, "АБВГ.301245.020", "Valid", "Steel", 1m);
        var persistence = new FakePersistence([]);
        var service = new ImportService(new FakeCadFactory([partial, valid],
            new Dictionary<string, string?> { ["partial.m3d"] = "Invalid component count schema." }),
            persistence, new FakeSourceStorage());

        var report = await service.ExecuteAsync(Guid.NewGuid(), Source, default);

        Assert.Equal(2, report.RejectedCount);
        Assert.All(report.Files, file => Assert.Equal(ImportFileStatus.Rejected, file.Status));
        Assert.Contains("count schema", report.Files.Single(x => x.FileName == "partial.m3d").Reason!);
        Assert.Contains("identity", report.Files.Single(x => x.FileName == "valid.m3d").Reason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Проверяет объединение повторных строк состава с положительным количеством и отражение объединения в отчёте.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Repeated_positive_component_rows_are_combined_and_reported()
    {
        var part = Doc("part.m3d", PdmObjectType.Part, "АБВГ.301245.001", mass: 1m);
        var assembly = Doc("assembly.a3d", PdmObjectType.Assembly, "АБВГ.301245.002", material: null,
            components: [new CadComponentDto
            {
                                File = "part.m3d",
                                Count = 2
            }, new CadComponentDto
{
        File = "part.m3d",
        Count = 3
}]);
        var (service, persistence) = Make([part, assembly], []);

        var report = await service.ExecuteAsync(Guid.NewGuid(), Source, default);

        var result = report.Files.Single(x => x.FileName == "assembly.a3d");
        Assert.Equal(ImportFileStatus.Accepted, result.Status);
        Assert.Contains("Repeated component rows were combined by file name.", result.Warnings);
        var assemblyId = persistence.LastPlan!.NewObjects.Single(x => x.Type == PdmObjectType.Assembly).Id;
        var version = persistence.LastPlan.NewVersions.Single(x => x.ObjectId == assemblyId);
        Assert.Equal(5, Assert.Single(version.Components).Quantity);
    }

    /// <summary>
    /// Проверяет, что единственная строка с недопустимым количеством не считается объединённой с дубликатами.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Single_invalid_quantity_does_not_claim_duplicate_rows_were_combined()
    {
        var assembly = Doc("assembly.a3d", PdmObjectType.Assembly, "АБВГ.301245.002", material: null,
            components: [new CadComponentDto
            {
                                File = "part.m3d",
                                Count = 0
            }]);
        var (service, _) = Make([assembly], []);

        var report = await service.ExecuteAsync(Guid.NewGuid(), Source, default);

        var result = Assert.Single(report.Files);
        Assert.Equal(ImportFileStatus.Rejected, result.Status);
        Assert.DoesNotContain(result.Warnings, x => x.Contains("Repeated component rows"));
    }

    /// <summary>
    /// Проверяет сохранение ссылки на исходный файл пакета для изменённой версии.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Changed_version_gets_source_reference_for_its_package_file()
    {
        var existing = Existing("АБВГ.301245.001", PdmObjectType.Part, "Old", 1m);
        var fileStorage = new FakeSourceStorage();
        var persistence = new FakePersistence([existing]);
        var service = new ImportService(new FakeCadFactory([Doc("new.m3d", PdmObjectType.Part,
            "АБВГ.301245.001", "New", "Steel", 2m)]), persistence, fileStorage);
        var importId = Guid.NewGuid();

        await service.ExecuteAsync(importId, Source, default);

        Assert.Equal($"{importId}/new.m3d", existing.CurrentVersion!.SourceReference);
    }

    /// <summary>
    /// Проверяет, что изменение только порядка компонентов не создаёт новую версию.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Reordered_components_do_not_create_a_version()
    {
        var a = Existing("АБВГ.301245.001", PdmObjectType.Part, "Part A", 1m);
        var b = Existing("АБВГ.301245.002", PdmObjectType.Part, "Part B", 2m);
        var assembly = Existing("АБВГ.301245.003", PdmObjectType.Assembly, "Top", null,
            VersionState.InWork, (a, 2), (b, 3));
        var import = Doc("top.a3d", PdmObjectType.Assembly, "АБВГ.301245.003", "Top",
            material: null, components: [new CadComponentDto
            {
                                File = "b.m3d",
                                Count = 3
            }, new CadComponentDto
{
        File = "a.m3d",
        Count = 2
}]);
        var (service, persistence) = Make([
            Doc("a.m3d", PdmObjectType.Part, "АБВГ.301245.001", "Part A", "Steel", 1m),
            Doc("b.m3d", PdmObjectType.Part, "АБВГ.301245.002", "Part B", "Steel", 2m), import
        ], [a, b, assembly]);

        var report = await service.ExecuteAsync(Guid.NewGuid(), Source, default);

        Assert.Equal(ImportFileAction.Unchanged, report.Files.Single(x => x.FileName == "top.a3d").Action);
        Assert.Empty(persistence.LastPlan!.NewVersions);
    }

    /// <summary>
    /// Проверяет создание следующей версии утверждённого объекта и повторное использование обновлённого объекта в работе.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Changed_approved_object_gets_next_version_and_updated_inwork_object_is_reused()
    {
        var approved = Existing("АБВГ.301245.001", PdmObjectType.Part, "A", 1m, state: VersionState.Approved);
        var inwork = Existing("АБВГ.301245.002", PdmObjectType.Part, "B", 1m);
        var (service, persistence) = Make([
            Doc("a.m3d", PdmObjectType.Part, "АБВГ.301245.001", "A2", "Steel", 2m),
            Doc("b.m3d", PdmObjectType.Part, "АБВГ.301245.002", "B2", "Steel", 2m)
        ], [approved, inwork]);

        var report = await service.ExecuteAsync(Guid.NewGuid(), Source, default);

        Assert.Equal(ImportFileAction.NewVersion, report.Files.Single(x => x.FileName == "a.m3d").Action);
        Assert.Equal(ImportFileAction.Updated, report.Files.Single(x => x.FileName == "b.m3d").Action);
        Assert.Equal(2, Assert.Single(persistence.LastPlan!.NewVersions).Version);
        Assert.Equal(1, approved.Versions.Max(x => x.Version));
        Assert.Equal("B2", inwork.CurrentVersion!.Name);
    }

    /// <summary>
    /// Проверяет, что для объекта только с отменёнными версиями используется номер выше максимального исторического.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task All_cancelled_object_uses_maximum_historical_version_plus_one()
    {
        var cancelled = Existing("АБВГ.301245.001", PdmObjectType.Part, "Old", 1m, VersionState.Cancelled);
        var previous = new ObjectVersion { ObjectId = cancelled.Id, Version = 7, State = VersionState.Cancelled, Name = "Earlier", Mass = 1m };
        cancelled.Versions.Add(previous);
        var (service, persistence) = Make([Doc("part.m3d", PdmObjectType.Part, "АБВГ.301245.001", "Current", "Steel", 2m)], [cancelled]);

        var report = await service.ExecuteAsync(Guid.NewGuid(), Source, default);

        Assert.Equal(ImportFileAction.NewVersion, Assert.Single(report.Files).Action);
        Assert.Equal(8, Assert.Single(persistence.LastPlan!.NewVersions).Version);
    }

    /// <summary>
    /// Проверяет создание нового объекта при изменении обозначения.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Changed_designation_creates_a_new_object()
    {
        var original = Existing("АБВГ.301245.001", PdmObjectType.Part, "Part", 1m);
        var (service, persistence) = Make([Doc("renamed.m3d", PdmObjectType.Part, "АБВГ.301245.099", "Part", "Steel", 1m)], [original]);

        var report = await service.ExecuteAsync(Guid.NewGuid(), Source, default);

        Assert.Equal(ImportFileAction.Created, Assert.Single(report.Files).Action);
        Assert.Single(persistence.LastPlan!.NewObjects);
        Assert.Equal("АБВГ.301245.099", persistence.LastPlan.NewObjects[0].Designation);
    }

    /// <summary>
    /// Проверяет допустимость повторной ветви и отклонение цикла пакета вместе с его предками.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Repeated_branch_is_valid_but_package_cycle_is_rejected_with_ancestors()
    {
        var leaf = Doc("leaf.m3d", PdmObjectType.Part, "АБВГ.301245.001", mass: 1m);
        var left = Doc("left.a3d", PdmObjectType.Assembly, "АБВГ.301245.002", material: null,
            components: [new CadComponentDto
            {
                                File = "leaf.m3d",
                                Count = 1
            }]);
        var right = Doc("right.a3d", PdmObjectType.Assembly, "АБВГ.301245.003", material: null,
            components: [new CadComponentDto
            {
                                File = "leaf.m3d",
                                Count = 2
            }]);
        var top = Doc("top.a3d", PdmObjectType.Assembly, "АБВГ.301245.004", material: null,
            components: [new CadComponentDto
            {
                                File = "left.a3d",
                                Count = 1
            }, new CadComponentDto
{
        File = "right.a3d",
        Count = 1
}]);
        var (validService, _) = Make([leaf, left, right, top], []);
        var validReport = await validService.ExecuteAsync(Guid.NewGuid(), Source, default);
        Assert.Equal(4, validReport.AcceptedCount);

        var a = Doc("a.a3d", PdmObjectType.Assembly, "АБВГ.301245.010", material: null,
            components: [new CadComponentDto
            {
                                File = "b.a3d",
                                Count = 1
            }, new CadComponentDto
{
        File = "d.a3d",
        Count = 1
}]);
        var b = Doc("b.a3d", PdmObjectType.Assembly, "АБВГ.301245.011", material: null,
            components: [new CadComponentDto
            {
                                File = "a.a3d",
                                Count = 1
            }]);
        var parent = Doc("parent.a3d", PdmObjectType.Assembly, "АБВГ.301245.012", material: null,
            components: [new CadComponentDto
            {
                                File = "a.a3d",
                                Count = 1
            }]);
        var d = Doc("d.a3d", PdmObjectType.Assembly, "АБВГ.301245.013", material: null,
            components: [new CadComponentDto
            {
                                File = "c.a3d",
                                Count = 1
            }]);
        var c = Doc("c.a3d", PdmObjectType.Assembly, "АБВГ.301245.014", material: null,
            components: [new CadComponentDto
            {
                                File = "a.a3d",
                                Count = 1
            }]);
        var (cycleService, _) = Make([a, b, c, d, parent], []);
        var cycleReport = await cycleService.ExecuteAsync(Guid.NewGuid(), Source, default);
        Assert.Equal(5, cycleReport.RejectedCount);
    }

    /// <summary>
    /// Проверяет, что цикл, найденный только в базе данных, останавливает импорт до продвижения файлов.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Database_only_cycle_blocks_import_before_file_promotion()
    {
        var first = new PdmObject { Type = PdmObjectType.Assembly, Designation = "АБВГ.301245.010" };
        var second = new PdmObject { Type = PdmObjectType.Assembly, Designation = "АБВГ.301245.011" };
        var files = new FakeSourceStorage();
        var persistence = new FakePersistence([first, second], [new ActiveGraphEdgeDto
        {
                        ParentId = first.Id,
                        ChildId = second.Id
        }, new ActiveGraphEdgeDto
{
        ParentId = second.Id,
        ChildId = first.Id
}]);
        var service = new ImportService(new FakeCadFactory([Doc("part.m3d", PdmObjectType.Part,
            "АБВГ.301245.001", mass: 1m)]), persistence, files);

        await Assert.ThrowsAsync<MiniPdm.Common.Exceptions.BusinessLogicException>(() => service.ExecuteAsync(Guid.NewGuid(), Source, default));
        Assert.Equal(0, files.PromoteCount);
    }

    /// <summary>
    /// Проверяет компенсацию продвинутых файлов после подтверждённого отката транзакции.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Confirmed_rollback_compensates_promoted_files()
    {
        var files = new FakeSourceStorage();
        var persistence = new FakePersistence([], rollback: true);
        var service = new ImportService(new FakeCadFactory([Doc("part.m3d", PdmObjectType.Part,
            "АБВГ.301245.001", mass: 1m)]), persistence, files);

        await Assert.ThrowsAsync<ImportSaveException>(() => service.ExecuteAsync(Guid.NewGuid(), Source, default));
        Assert.Equal(1, files.PromoteCount);
        Assert.Equal(1, files.CompensateCount);
    }

    /// <summary>
    /// Проверяет повторное получение отчёта завершённого импорта без повторного чтения источника.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task Completed_import_id_replays_report_without_reading_source_again()
    {
        var importId = Guid.NewGuid();
        var cad = new FakeCadFactory([Doc("part.m3d", PdmObjectType.Part, "АБВГ.301245.001", mass: 1m)]);
        var persistence = new FakePersistence([]);
        var service = new ImportService(cad, persistence, new FakeSourceStorage());

        var first = await service.ExecuteAsync(importId, Source, default);
        cad.OpenCount = 0;
        var second = await service.ExecuteAsync(importId, Source, default);

        Assert.Equal(first.ImportId, second.ImportId);
        Assert.Equal(first.AcceptedCount, second.AcceptedCount);
        Assert.Equal(first.Files.Select(x => x.FileName), second.Files.Select(x => x.FileName));
        Assert.Equal(0, cad.OpenCount);
    }

    private static (ImportService Service, FakePersistence Persistence) Make(IReadOnlyList<CadDocumentDto> docs, IReadOnlyList<PdmObject> existing)
    {
        var cad = new FakeCadFactory(docs);
        var persistence = new FakePersistence(existing);
        return (new ImportService(cad, persistence, new FakeSourceStorage()), persistence);
    }

    private static readonly CadSourceDescriptorDto Source = new()
    {
        Kind = "fake",
        Location = "memory"
    };

    private static CadDocumentDto Doc(string file, PdmObjectType type, string? designation, string name = "Item",
        string? material = "Steel", decimal? mass = null, IReadOnlyList<CadComponentDto>? components = null) =>
        new CadDocumentDto
        {
            FileName = file,
            Type = type,
            Designation = designation,
            Name = name,
            Material = material,
            Mass = mass,
            Components = components ?? []
        };

    private static PdmObject Existing(string designation, PdmObjectType type, string name, decimal? mass,
        VersionState state = VersionState.InWork, params (PdmObject Child, int Count)[] components)
    {
        var item = new PdmObject { Type = type, Designation = designation };
        var version = new ObjectVersion
        {
            ObjectId = item.Id,
            Version = 1,
            State = state,
            Name = name,
            Material = type == PdmObjectType.Part ? "Steel" : null,
            Mass = mass
        };
        foreach (var (child, count) in components)
            version.Components.Add(new BomLink { ParentVersionId = version.Id, ChildObjectId = child.Id, Quantity = count });
        item.Versions.Add(version);
        item.CurrentVersion = state == VersionState.Cancelled ? null : version;
        item.CurrentVersionId = item.CurrentVersion?.Id;
        return item;
    }

    private sealed class FakeCadFactory(IReadOnlyList<CadDocumentDto> docs, IReadOnlyDictionary<string, string?>? errors = null) : ICadSourceFactory
    {
        /// <summary>
        /// Число открытий источника документов тестовым фабричным адаптером.
        /// </summary>
        public int OpenCount
        {
            get; set;
        }
        public Task<ICadSession> OpenAsync(CadSourceDescriptorDto descriptor, CancellationToken cancellationToken)
        {
            OpenCount++;
            return Task.FromResult<ICadSession>(new FakeSession(docs, errors ?? new Dictionary<string, string?>()));
        }
    }

    private sealed class FakeSession(IReadOnlyList<CadDocumentDto> docs, IReadOnlyDictionary<string, string?> errors) : ICadSession
    {
        /// <summary>
        /// Источник документов, предоставляемый тестовой сессией.
        /// </summary>
        public ICadDocumentSource Source { get; } = new FakeDocumentSource(docs);

        /// <summary>
        /// Читатель документов, предоставляемый тестовой сессией.
        /// </summary>
        public ICadDocumentReader Reader { get; } = new FakeReader(docs, errors);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeDocumentSource(IReadOnlyList<CadDocumentDto> docs) : ICadDocumentSource
    {
        public async IAsyncEnumerable<CadDocumentRefDto> GetDocumentsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var doc in docs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new CadDocumentRefDto
                {
                    FileName = doc.FileName
                };
                await Task.Yield();
            }
        }
    }

    private sealed class FakeReader(IReadOnlyList<CadDocumentDto> docs, IReadOnlyDictionary<string, string?> errors) : ICadDocumentReader
    {
        public Task<CadReadResultDto> ReadAsync(CadDocumentRefDto document, CancellationToken cancellationToken) =>
            Task.FromResult(new CadReadResultDto
            {
                Document = docs.Single(x => x.FileName == document.FileName),
                Error = errors.GetValueOrDefault(document.FileName)
            });
    }

    private sealed class FakeSourceStorage : IImportSourceStorage
    {
        /// <summary>
        /// Число вызовов продвижения файлов в тестовом хранилище импорта.
        /// </summary>
        public int PromoteCount
        {
            get; private set;
        }
        /// <summary>
        /// Число вызовов компенсации файлов в тестовом хранилище импорта.
        /// </summary>
        public int CompensateCount
        {
            get; private set;
        }

        /// <summary>
        /// Копирует принятые файлы в постоянное хранилище для указанного импорта.
        /// </summary>
        /// <param name="importId">Идентификатор импорта.</param>
        /// <param name="source">Описание источника CAD.</param>
        /// <param name="acceptedFiles">Имена принятых файлов.</param>
        /// <param name="ct">Токен отмены операции.</param>
        /// <returns>Задача завершается после выполнения проверок теста.</returns>
        public Task PromoteAsync(Guid importId, CadSourceDescriptorDto source, IReadOnlyCollection<string> acceptedFiles, CancellationToken ct)
        {
            PromoteCount++;
            return Task.CompletedTask;
        }
        public string GetSourceReference(Guid importId, string fileName) => $"{importId}/{fileName}";

        /// <summary>
        /// Удаляет файлы, продвинутые для импорта, при компенсации отката.
        /// </summary>
        /// <param name="importId">Идентификатор импорта.</param>
        /// <param name="ct">Токен отмены операции.</param>
        /// <returns>Задача завершается после выполнения проверок теста.</returns>
        public Task CompensateAsync(Guid importId, CancellationToken ct)
        {
            CompensateCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePersistence(IReadOnlyList<PdmObject> existing, IReadOnlyList<ActiveGraphEdgeDto>? graph = null, bool rollback = false) : IImportDatabaseService
    {
        private readonly Dictionary<Guid, ImportPersistenceResultDto> _completed = [];
        /// <summary>
        /// Последний план записи, переданный тестовой реализации хранилища.
        /// </summary>
        public ImportWritePlanDto? LastPlan
        {
            get; private set;
        }
        public async Task<ImportPersistenceResultDto> ExecuteAsync(Guid importId, ImportLookupDto lookup,
            Func<ImportSnapshotDto, CancellationToken, Task<ImportWritePlanDto>> prepare, CancellationToken ct)
        {
            if (_completed.TryGetValue(importId, out var replay))
                return replay with
                {
                    Replayed = true
                };
            LastPlan = await prepare(new ImportSnapshotDto
            {
                ExistingObjects = existing,
                CurrentGraph = graph ?? []
            }, ct);
            if (rollback)
                return new ImportPersistenceResultDto
                {
                    State = ImportCommitState.ConfirmedRollback,
                    Replayed = false,
                    ReportJson = null,
                    Error = "test rollback"
                };
            var result = new ImportPersistenceResultDto
            {
                State = ImportCommitState.Completed,
                Replayed = false,
                ReportJson = LastPlan.ReportJson
            };
            _completed[importId] = result;
            return result;
        }
        public Task<ImportPersistenceResultDto?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(_completed.GetValueOrDefault(id));
        public Task<ImportPersistenceResultDto> ResolveAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(_completed.GetValueOrDefault(id) ?? new ImportPersistenceResultDto
            {
                State = ImportCommitState.ConfirmedRollback,
                Replayed = false,
                ReportJson = null
            });
        public async Task<bool> CompensateIfRolledBackAsync(Guid id, Func<CancellationToken, Task> compensate, CancellationToken ct)
        {
            if (!rollback)
                return false;
            await compensate(ct);
            return true;
        }
    }
}
