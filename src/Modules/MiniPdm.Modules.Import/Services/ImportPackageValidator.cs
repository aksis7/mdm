using Resources = MiniPdm.Common.Resources;
using MiniPdm.Contracts.Modules.Import.DtoModels;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Composition;
using MiniPdm.Domain.Versions;
using MiniPdm.Modules.Import.Abstractions.Cad;
using MiniPdm.Modules.Import.DtoModels.Cad;
using MiniPdm.Modules.Import.Abstractions.Database;
using MiniPdm.Modules.Import.DtoModels.Database;

namespace MiniPdm.Modules.Import.Services;

/// <summary>
/// Проверяет документы пакета и подготавливает ключи поиска и отчёт проверки.
/// </summary>
/// <param name="input">Результаты чтения файлов до проверки пакета.</param>
internal sealed class ImportPackageValidator(IReadOnlyList<ImportPackageValidator.FileEntry> input)
{
    /// <summary>
    /// Состояние проверки одного файла пакета.
    /// </summary>
    /// <param name="fileName">Имя файла из источника.</param>
    /// <param name="document">Прочитанный документ или <see langword="null"/>.</param>
    /// <param name="readError">Ошибка чтения, если она возникла.</param>
    internal sealed class FileEntry(string fileName, CadDocumentDto? document, string? readError)
    {
        /// <summary>
        /// Имя файла в пакете.
        /// </summary>
        public string FileName { get; } = fileName;
        /// <summary>
        /// Прочитанные данные файла, если чтение удалось.
        /// </summary>
        public CadDocumentDto? Document { get; } = document;
        /// <summary>
        /// Причина отклонения файла; значение можно установить при проверке пакета.
        /// </summary>
        public string? Reason { get; set; } = readError;
        /// <summary>
        /// Предупреждения проверки для строки отчёта.
        /// </summary>
        public List<string> Warnings { get; } = [];
        /// <summary>
        /// Количество вхождений компонентов, индексированных по имени файла.
        /// </summary>
        public Dictionary<string, int> ComponentCounts { get; } = new(StringComparer.Ordinal);
        /// <summary>
        /// Действие, которое импорт выполнит для принятого файла.
        /// </summary>
        public ImportFileAction? Action
        {
            get; set;
        }
        /// <summary>
        /// Возвращает <see langword="true"/>, если причина отклонения отсутствует.
        /// </summary>
        public bool Accepted => Reason is null;
        /// <summary>
        /// Возвращает ключ идентичности PDM-объекта или ключ файла при ошибке чтения.
        /// </summary>
        public string IdentityKey => Document is null ? $"file:{FileName}" : GetIdentityKey(Document);
        /// <summary>
        /// Создаёт строку отчёта по текущему состоянию проверки файла.
        /// </summary>
        /// <returns>DTO-строка со статусом, причиной, действием и предупреждениями.</returns>
        public ImportFileResultDto ToDto() => new()
        {
            FileName = FileName,
            Status = Accepted ? ImportFileStatus.Accepted : ImportFileStatus.Rejected,
            Reason = Reason,
            Action = Action,
            Warnings = Warnings.ToArray()
        };
    }

    private readonly List<FileEntry> _files = input.ToList();
    private readonly Dictionary<string, FileEntry> _byFile = new(StringComparer.Ordinal);

    /// <summary>
    /// Файлы пакета в исходном порядке обработки.
    /// </summary>
    public IReadOnlyList<FileEntry> Files => _files;
    /// <summary>
    /// Индекс первого файла по его имени, построенный после проверки.
    /// </summary>
    public IReadOnlyDictionary<string, FileEntry> ByFile => _byFile;

    /// <summary>
    /// Проверяет идентичности, атрибуты, ссылки и циклы пакета.
    /// </summary>
    public void Validate()
    {
        foreach (var grouping in _files.GroupBy(x => x.FileName, StringComparer.Ordinal).Where(x => x.Count() > 1))
            foreach (var file in grouping)
                Reject(file, Resources.BusinessLogicException.ImportDuplicateNames);
        foreach (var file in _files)
        {
            if (file.Document is null)
            {
                if (file.Reason is null)
                    Reject(file, Resources.BusinessLogicException.ImportMissingDocument);
                continue;
            }
            ValidateAttributes(file);
            ValidateComponents(file);
        }

        foreach (var group in _files.Where(x => x.Document is not null && HasIdentity(x.Document))
                     .GroupBy(x => x.IdentityKey, StringComparer.Ordinal).Where(x => x.Count() > 1))
            foreach (var file in group)
                Reject(file, Resources.BusinessLogicException.ImportDuplicateIdentity);

        _byFile.Clear();
        foreach (var file in _files)
            if (!_byFile.ContainsKey(file.FileName))
                _byFile.Add(file.FileName, file);

        RejectBadReferences();
        RejectPackageCycles();
    }

    /// <summary>
    /// Формирует ключи для поиска существующих объектов в базе данных.
    /// </summary>
    /// <returns>Обозначения и нормализованные имена стандартных деталей.</returns>
    public ImportLookupDto CreateLookup()
    {
        var docs = _files.Where(x => x.Document is not null).Select(x => x.Document!).ToArray();
        return new ImportLookupDto
        {
            Designations = docs.Where(x => x.Designation is not null).Select(x => x.Designation!).Distinct(StringComparer.Ordinal).ToArray(),
            NormalizedStandardNames = docs.Where(x => x.Type == PdmObjectType.StandardPart && !string.IsNullOrWhiteSpace(x.Name)).Select(x => ObjectIdentity.NormalizeStandardName(x.Name)).Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    /// <summary>
    /// Создаёт отчёт из текущих результатов проверки.
    /// </summary>
    /// <param name="importId">Идентификатор операции импорта.</param>
    /// <returns>Отчёт со строкой результата для каждого файла.</returns>
    public ImportReportDto ToReport(Guid importId) => new()
    {
        ImportId = importId,
        Files = _files.Select(x => x.ToDto()).ToArray()
    };

    private void ValidateAttributes(FileEntry file)
    {
        var d = file.Document!;
        var validation = VersionAttributeRules.Validate(d.Type, d.Name, d.Material, d.Mass);
        foreach (var error in validation.Errors.Where(x => x.Scope == VersionAttributeRuleScope.Common))
            Reject(file, error.Message);
        if (d.Type is PdmObjectType.Assembly or PdmObjectType.Part)
        {
            if (d.Designation is null || !ObjectIdentity.IsValidDesignation(d.Designation))
                Reject(file, Resources.InputLogicException.DesignationRequired);
        }
        else if (d.Type == PdmObjectType.StandardPart && d.Designation is not null)
            Reject(file, Resources.BusinessLogicException.StandardPartDesignationInvalid);
        foreach (var error in validation.Errors.Where(x => x.Scope == VersionAttributeRuleScope.TypeSpecific))
            Reject(file, error.Message);
        file.Warnings.AddRange(validation.Warnings.Select(x => x.Message));
    }

    private static void ValidateComponents(FileEntry file)
    {
        var d = file.Document!;
        if (d.Type != PdmObjectType.Assembly && d.Components.Count != 0)
            Reject(file, Resources.InputLogicException.CompositionMustBeAssembly);
        var validRows = new List<(string Child, int Quantity)>();
        foreach (var component in d.Components)
        {
            if (string.IsNullOrWhiteSpace(component.File))
            {
                Reject(file, Resources.BusinessLogicException.ComponentFilenameRequired);
                continue;
            }
            if (component.Count <= 0)
            {
                Reject(file, string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.InputLogicException.ComponentCountPositive, component.File));
                continue;
            }
            validRows.Add((component.File, component.Count));
        }
        var normalized = CompositionRules.Normalize(validRows);
        foreach (var error in normalized.Errors)
            Reject(file, error);
        foreach (var pair in normalized.Items)
            file.ComponentCounts[pair.Key] = pair.Value;
        if (normalized.Warnings.Count != 0 && file.Reason is null)
            file.Warnings.Add(Resources.BusinessLogicException.RepeatedComponentRowsCombinedByFile);
    }

    private void RejectBadReferences()
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var file in _files.Where(x => x.Accepted && x.Document?.Type == PdmObjectType.Assembly))
            {
                foreach (var componentName in file.ComponentCounts.Keys)
                {
                    if (!_byFile.TryGetValue(componentName, out var child))
                    {
                        Reject(file, string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.InputLogicException.ComponentFileMissing, componentName));
                        changed = true;
                        break;
                    }
                    if (!child.Accepted)
                    {
                        Reject(file, string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.InputLogicException.ComponentFileRejected, componentName));
                        changed = true;
                        break;
                    }
                }
            }
        }
    }

    private void RejectPackageCycles()
    {
        var cyclic = new HashSet<FileEntry>();
        var assemblies = _files.Where(x => x.Accepted && x.Document?.Type == PdmObjectType.Assembly).ToArray();
        foreach (var start in assemblies)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<FileEntry>();
            foreach (var childName in start.ComponentCounts.Keys)
                if (_byFile.TryGetValue(childName, out var child) && child.Accepted && child.Document?.Type == PdmObjectType.Assembly)
                    pending.Push(child);
            while (pending.TryPop(out var current))
            {
                if (ReferenceEquals(current, start))
                {
                    cyclic.Add(start);
                    break;
                }
                if (!seen.Add(current.FileName))
                    continue;
                foreach (var childName in current.ComponentCounts.Keys)
                    if (_byFile.TryGetValue(childName, out var child) && child.Accepted && child.Document?.Type == PdmObjectType.Assembly)
                        pending.Push(child);
            }
        }
        foreach (var file in cyclic)
            Reject(file, Resources.BusinessLogicException.PackageCycle);
        RejectBadReferences();
    }

    internal static string GetIdentityKey(CadDocumentDto d) => d.Type == PdmObjectType.StandardPart
        ? "N:" + ObjectIdentity.NormalizeStandardName(d.Name)
        : "D:" + (d.Designation ?? "");
    private static bool HasIdentity(CadDocumentDto d) => d.Type == PdmObjectType.StandardPart
        ? !string.IsNullOrWhiteSpace(d.Name)
        : !string.IsNullOrWhiteSpace(d.Designation);
    private static void Reject(FileEntry file, string reason)
    {
        file.Reason ??= reason;
        file.Action = null;
    }
}
