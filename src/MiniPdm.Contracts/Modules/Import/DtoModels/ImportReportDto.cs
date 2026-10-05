namespace MiniPdm.Contracts.Modules.Import.DtoModels;

/// <summary>
/// Определяет результат обработки файла импорта.
/// </summary>
public enum ImportFileStatus
{
    /// <summary>
    /// Файл принят и обработан.
    /// </summary>
    Accepted,

    /// <summary>
    /// Файл отклонён и не обработан.
    /// </summary>
    Rejected
}

/// <summary>
/// Определяет действие, выполненное при обработке файла.
/// </summary>
public enum ImportFileAction
{
    /// <summary>
    /// Создан новый объект.
    /// </summary>
    Created,

    /// <summary>
    /// Обновлены данные существующего объекта.
    /// </summary>
    Updated,

    /// <summary>
    /// Создана новая версия существующего объекта.
    /// </summary>
    NewVersion,

    /// <summary>
    /// Файл совпадает с уже сохранёнными данными, изменений не потребовалось.
    /// </summary>
    Unchanged
}

/// <summary>
/// Результат обработки одного файла в пакете импорта.
/// </summary>
public sealed record ImportFileResultDto
{
    /// <summary>
    /// Имя обработанного файла.
    /// </summary>
    public string FileName { get; init; } = default!;

    /// <summary>
    /// Статус обработки файла.
    /// </summary>
    public ImportFileStatus Status
    {
        get; init;
    }

    /// <summary>
    /// Причина отклонения либо <see langword="null"/>.
    /// </summary>
    public string? Reason
    {
        get; init;
    }

    /// <summary>
    /// Выполненное действие либо <see langword="null"/>, если файл отклонён.
    /// </summary>
    public ImportFileAction? Action
    {
        get; init;
    }

    /// <summary>
    /// Предупреждения, сформированные при обработке файла.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = default!;

}

/// <summary>
/// Сводный отчёт об обработке пакета импорта.
/// </summary>
public sealed record ImportReportDto
{
    /// <summary>
    /// Идентификатор операции импорта.
    /// </summary>
    public Guid ImportId
    {
        get; init;
    }

    /// <summary>
    /// Результаты обработки отдельных файлов.
    /// </summary>
    public IReadOnlyList<ImportFileResultDto> Files { get; init; } = default!;


    /// <summary>
    /// Возвращает число файлов, принятых при импорте.
    /// </summary>
    public int AcceptedCount => Files.Count(x => x.Status == ImportFileStatus.Accepted);

    /// <summary>
    /// Возвращает число файлов, отклонённых при импорте.
    /// </summary>
    public int RejectedCount => Files.Count(x => x.Status == ImportFileStatus.Rejected);

    /// <summary>
    /// Возвращает число принятых файлов, для которых сформированы предупреждения.
    /// </summary>
    public int WarningCount => Files.Count(x => x.Status == ImportFileStatus.Accepted && x.Warnings.Count > 0);
}
