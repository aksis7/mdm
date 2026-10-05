namespace MiniPdm.Common.Exceptions;

/// <summary>
/// Представляет нарушение бизнес-правила, предназначенное для отображения пользователю.
/// </summary>
public sealed class BusinessLogicException : Exception
{
    /// <summary>
    /// Создаёт исключение с готовым текстом для пользователя.
    /// </summary>
    /// <param name="message">Полный текст сообщения.</param>
    public BusinessLogicException(string message) : base(message)
    {
    }
}
