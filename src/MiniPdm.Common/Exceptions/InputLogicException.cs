namespace MiniPdm.Common.Exceptions;

/// <summary>
/// Представляет ошибку входных данных, предназначенную для отображения пользователю.
/// </summary>
public sealed class InputLogicException : Exception
{
    /// <summary>
    /// Создаёт исключение с готовым текстом для пользователя.
    /// </summary>
    /// <param name="message">Полный текст сообщения.</param>
    public InputLogicException(string message) : base(message)
    {
    }
}
