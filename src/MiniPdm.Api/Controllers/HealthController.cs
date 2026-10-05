using Microsoft.AspNetCore.Mvc;

namespace MiniPdm.Api.Controllers;

/// <summary>
/// Предоставляет конечные точки для проверки доступности приложения.
/// </summary>
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    /// <summary>
    /// Возвращает положительный ответ, если приложение отвечает на запрос.
    /// </summary>
    /// <returns>HTTP-ответ со статусом и значением доступности.</returns>
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok" });
}
