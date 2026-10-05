using MiniPdm.Modules.Import.DtoModels.Cad;

namespace MiniPdm.Modules.Import.Abstractions.Cad;

/// <summary>
/// Открывает CAD-источник определённого формата.
/// </summary>
public interface ICadSourceAdapter
{
    /// <summary>
    /// Идентификатор поддерживаемого вида источника.
    /// </summary>
    string Kind
    {
        get;
    }
    /// <summary>
    /// Открывает сессию для заданного источника.
    /// </summary>
    /// <param name="descriptor">Вид и расположение источника.</param>
    /// <param name="cancellationToken">Токен отмены открытия.</param>
    /// <returns>Асинхронная задача с открытой сессией чтения.</returns>
    Task<ICadSession> OpenAsync(CadSourceDescriptorDto descriptor, CancellationToken cancellationToken);
}
