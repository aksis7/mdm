using MiniPdm.Modules.Import.DtoModels.Cad;

namespace MiniPdm.Modules.Import.Abstractions.Cad;

/// <summary>
/// Выбирает адаптер и открывает CAD-источник по его описателю.
/// </summary>
public interface ICadSourceFactory
{
    /// <summary>
    /// Открывает сессию чтения подходящего вида источника.
    /// </summary>
    /// <param name="descriptor">Вид и расположение источника.</param>
    /// <param name="cancellationToken">Токен отмены открытия.</param>
    /// <returns>Асинхронная задача с открытой сессией.</returns>
    Task<ICadSession> OpenAsync(CadSourceDescriptorDto descriptor, CancellationToken cancellationToken);
}
