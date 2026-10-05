using MediatR;
using MiniPdm.Domain.Versions.Mutations;
using MiniPdm.Modules.Versions.Abstractions;

namespace MiniPdm.Modules.Versions.Features.Commands.CloneVersion;

/// <summary>
/// Запрашивает создание новой версии на основе выбранной версии объекта.
/// </summary>
public sealed record CloneVersionCommand(Guid ObjectId, int SourceVersion, Guid ExpectedConcurrencyToken)
    : IRequest<VersionMutationResult>
{
    /// <summary>
    /// Идентификатор объекта, чья версия клонируется.
    /// </summary>
    public Guid ObjectId { get; init; } = ObjectId;

    /// <summary>
    /// Номер версии-источника.
    /// </summary>
    public int SourceVersion { get; init; } = SourceVersion;

    /// <summary>
    /// Токен объекта, прочитанный клиентом до изменения.
    /// </summary>
    public Guid ExpectedConcurrencyToken { get; init; } = ExpectedConcurrencyToken;
}

/// <summary>
/// Передаёт запрос клонирования сервису мутаций версий.
/// </summary>
/// <param name="service">Сервис выполнения мутаций версии.</param>
public sealed class CloneVersionCommandHandler(IVersionMutationService service)
    : IRequestHandler<CloneVersionCommand, VersionMutationResult>
{
    /// <summary>
    /// Создаёт версию из заданной версии объекта.
    /// </summary>
    /// <param name="request">Параметры клонирования.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача с результатом операции клонирования.</returns>
    public Task<VersionMutationResult> Handle(CloneVersionCommand request, CancellationToken cancellationToken) =>
        service.CloneAsync(request.ObjectId, request.SourceVersion, request.ExpectedConcurrencyToken, cancellationToken);
}
