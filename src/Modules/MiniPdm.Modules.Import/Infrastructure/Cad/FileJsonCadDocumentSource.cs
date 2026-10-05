using MiniPdm.Modules.Import.Abstractions.Cad;
using MiniPdm.Modules.Import.DtoModels.Cad;

namespace MiniPdm.Modules.Import.Infrastructure.Cad;

/// <summary>
/// Перечисляет поддерживаемые CAD-файлы в файловом каталоге.
/// </summary>
/// <param name="directory">Каталог с документами.</param>
public sealed class FileJsonCadDocumentSource(string directory) : ICadDocumentSource
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
                ".a3d",
                ".m3d"
    };

    /// <inheritdoc />
    public async IAsyncEnumerable<CadDocumentRefDto> GetDocumentsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var path in Directory.EnumerateFiles(directory)
                     .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                     .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new CadDocumentRefDto
            {
                FileName = Path.GetFileName(path)
            };
            await Task.Yield();
        }
    }
}
