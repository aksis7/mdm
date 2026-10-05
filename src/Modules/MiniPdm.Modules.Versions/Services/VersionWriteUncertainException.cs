namespace MiniPdm.Modules.Versions.Services;

/// <summary>
/// Исход фиксации версии неизвестен, поэтому автоматический повтор операции небезопасен.
/// </summary>
/// <param name="message">Сообщение о неопределённом исходе записи.</param>
/// <param name="innerException">Исключение, возникшее при попытке фиксации.</param>
public sealed class VersionWriteUncertainException(string message, Exception innerException) : Exception(message, innerException);
