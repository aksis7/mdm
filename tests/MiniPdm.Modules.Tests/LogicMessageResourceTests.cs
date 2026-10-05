using System.ComponentModel.DataAnnotations;
using MiniPdm.Common.Resources;
using MiniPdm.Contracts.Modules.BackgroundTasks.DtoModels;
using Xunit;

namespace MiniPdm.Modules.Tests;

/// <summary>
/// Проверяет доступность ресурсов ошибок через код и DataAnnotations.
/// </summary>
public sealed class LogicMessageResourceTests
{
    /// <summary>
    /// Проверяет, что тексты ресурсов загружаются и поддерживают подстановку данных диагностики.
    /// </summary>
    [Fact]
    public void Resource_messages_load_and_format_dynamic_diagnostics()
    {
        Assert.Equal("Object ID must not be empty.", InputLogicException.ObjectIdRequired);
        Assert.Equal("Component file 'wheel.m3d' is missing from this package.",
            string.Format(System.Globalization.CultureInfo.InvariantCulture, InputLogicException.ComponentFileMissing, "wheel.m3d"));
        Assert.False(string.IsNullOrWhiteSpace(BusinessLogicException.CompositionCycle));
    }

    /// <summary>
    /// Проверяет, что некорректный интервал использует сообщение из общей библиотеки ресурсов.
    /// </summary>
    [Fact]
    public void Background_schedule_range_uses_input_resource()
    {
        var request = new UpdateBackgroundTaskScheduleRequestDto { IntervalMinutes = 0 };
        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, result => result.ErrorMessage == "The field IntervalMinutes must be between 1 and 525600.");
    }
}
