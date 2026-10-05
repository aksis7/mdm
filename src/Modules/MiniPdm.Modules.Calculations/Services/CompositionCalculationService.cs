using MiniPdm.Contracts.Modules.Calculations.DtoModels;
using MiniPdm.Domain.Calculations;
using MiniPdm.Modules.Composition.DtoModels;
using MiniPdm.Modules.Composition.Services;

namespace MiniPdm.Modules.Calculations.Services;

/// <summary>
/// Рассчитывает общую массу и сводную спецификацию объекта по его дереву состава.
/// Неполные данные и циклы отображаются как диагностические сообщения результата.
/// </summary>
/// <param name="compositionReadService">Сервис чтения вхождений дерева состава.</param>
public sealed class CompositionCalculationService(CompositionReadService compositionReadService)
{
    /// <summary>
    /// Получает дерево объекта и формирует публичный результат расчёта.
    /// </summary>
    /// <param name="objectId">Идентификатор корневого объекта.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Результат расчёта или <see langword="null"/>, если объект отсутствует.</returns>
    public async Task<CompositionCalculationDto?> GetCalculationAsync(Guid objectId, CancellationToken cancellationToken)
    {
        var occurrences = await compositionReadService.ReadAsync(objectId, cancellationToken);
        return occurrences.Count == 0 ? null : MapCalculationDto(objectId, occurrences);
    }

    /// <summary>
    /// Преобразует прочитанные вхождения в входные данные калькулятора и DTO ответа.
    /// </summary>
    /// <param name="objectId">Идентификатор корневого объекта расчёта.</param>
    /// <param name="occurrences">Вхождения объектов, прочитанные из дерева состава.</param>
    /// <returns>Публичный расчёт массы и плоской спецификации.</returns>
    public static CompositionCalculationDto MapCalculationDto(Guid objectId, IReadOnlyList<CompositionOccurrenceDto> occurrences)
    {
        var inputs = occurrences.Select(occurrence => new CompositionCalculationInput(
            occurrence.ObjectId, occurrence.ObjectPath, occurrence.ParentPath, occurrence.LocalQuantity,
            occurrence.Type, occurrence.Designation, occurrence.Name, occurrence.Material, occurrence.VersionId,
            occurrence.VersionNumber, occurrence.State, occurrence.UnitMassKg, occurrence.IsCycle));
        var result = CompositionCalculator.Calculate(objectId, inputs);
        return new CompositionCalculationDto
        {
            RootObjectId = result.RootObjectId,
            TotalMassKg = result.TotalMassKg,
            IsComplete = result.IsComplete,
            Items = result.Items.Select(item => new SpecificationItemDto
            {
                ObjectId = item.ObjectId,
                Type = item.Type.ToString(),
                Designation = item.Designation,
                Name = item.Name,
                Material = item.Material,
                VersionId = item.VersionId,
                VersionNumber = item.VersionNumber,
                Quantity = item.Quantity,
                UnitMassKg = item.UnitMassKg,
                TotalMassKg = item.TotalMassKg
            }).ToArray(),
            Diagnostics = result.Diagnostics.Select(diagnostic => new CalculationDiagnosticDto
            {
                Code = diagnostic.Code,
                ObjectId = diagnostic.ObjectId,
                ObjectPath = diagnostic.ObjectPath.ToArray(),
                Message = diagnostic.Message
            }).ToArray()
        };
    }
}
