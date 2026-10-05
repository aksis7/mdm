using System.ComponentModel.DataAnnotations;
using MiniPdm.Contracts.Modules.BackgroundTasks.DtoModels;
using Xunit;

namespace MiniPdm.Modules.Tests.BackgroundTasks.Features.Commands.UpdateBackgroundTaskSchedule;

/// <summary>
/// Проверяет ограничения интервала в публичном запросе изменения расписания.
/// </summary>
public sealed class BackgroundTaskRequestValidationTests
{
    /// <summary>
    /// Проверяет допустимые и недопустимые интервалы расписания через стандартную валидацию модели.
    /// </summary>
    [Fact]
    public void Schedule_interval_is_validated_on_dto_property()
    {
        Assert.False(IsValid(0));
        Assert.True(IsValid(1));
        Assert.True(IsValid(525600));
        Assert.False(IsValid(525601));
    }

    private static bool IsValid(int intervalMinutes)
    {
        var request = new UpdateBackgroundTaskScheduleRequestDto
        {
            IntervalMinutes = intervalMinutes
        };
        return Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), validateAllProperties: true);
    }
}
