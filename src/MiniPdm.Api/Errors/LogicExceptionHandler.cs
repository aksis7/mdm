using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MiniPdm.Common.Exceptions;

namespace MiniPdm.Api.Errors;

/// <summary>
/// Преобразует известные ошибки ввода и бизнес-правил в HTTP-ответы Problem Details.
/// </summary>
public sealed class LogicExceptionHandler : IExceptionHandler
{
    /// <summary>
    /// Обрабатывает известные ошибки логики, передавая остальные исключения серверному конвейеру.
    /// </summary>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <param name="exception">The exception raised by request processing.</param>
    /// <param name="cancellationToken">A token indicating that the response was cancelled.</param>
    /// <returns><see langword="true"/>, если известное исключение логики обработано.</returns>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var statusCode = exception switch
        {
            InputLogicException => StatusCodes.Status400BadRequest,
            BusinessLogicException => StatusCodes.Status409Conflict,
            _ => 0
        };
        if (statusCode == 0)
            return false;

        httpContext.Response.StatusCode = statusCode;
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode == StatusCodes.Status400BadRequest ? MiniPdm.Common.Resources.InputLogicException.ProblemTitle : MiniPdm.Common.Resources.BusinessLogicException.ProblemTitle,
            Detail = exception.Message
        };
        await httpContext.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
        return true;
    }
}
