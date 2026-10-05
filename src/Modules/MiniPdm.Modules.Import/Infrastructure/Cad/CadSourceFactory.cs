using MiniPdm.Modules.Import.Abstractions.Cad;
using MiniPdm.Modules.Import.DtoModels.Cad;

namespace MiniPdm.Modules.Import.Infrastructure.Cad;

/// <summary>
/// Выбирает зарегистрированный адаптер по виду CAD-источника.
/// </summary>
/// <param name="adapters">Адаптеры, поддерживаемые приложением.</param>
public sealed class CadSourceFactory(IEnumerable<ICadSourceAdapter> adapters) : ICadSourceFactory
{
    private readonly IReadOnlyDictionary<string, ICadSourceAdapter> _adapters = adapters
        .ToDictionary(adapter => adapter.Kind, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<ICadSession> OpenAsync(CadSourceDescriptorDto descriptor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_adapters.TryGetValue(descriptor.Kind, out var adapter))
            throw new NotSupportedException($"CAD source kind '{descriptor.Kind}' is not registered.");
        return adapter.OpenAsync(descriptor, cancellationToken);
    }
}
