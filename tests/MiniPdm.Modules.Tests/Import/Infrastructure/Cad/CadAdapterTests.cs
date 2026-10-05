using MiniPdm.Modules.Import.Abstractions.Cad;
using MiniPdm.Modules.Import.DtoModels.Cad;
using MiniPdm.Modules.Import.Infrastructure.Cad;
using Xunit;

namespace MiniPdm.Modules.Tests.Import.Infrastructure.Cad;

/// <summary>
/// Проверяет чтение CAD-файлов, фильтрацию поддерживаемых форматов и диагностику некорректных документов.
/// </summary>
public sealed class CadAdapterTests
{
    /// <summary>
    /// Проверяет перечисление только поддерживаемых CAD-файлов с использованием имени файла на диске.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task SourceListsOnlySupportedCadFilesByDiskFileName()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.WriteAsync("unit.a3d", "{}");
        await fixture.WriteAsync("part.M3D", "{}");
        await fixture.WriteAsync("readme.txt", "ignored");
        await using var session = await fixture.OpenAsync();

        var names = new List<string>();
        await foreach (var document in session.Source.GetDocumentsAsync(CancellationToken.None))
            names.Add(document.FileName);

        Assert.Equal(new[] { "part.M3D", "unit.a3d" }, names);
    }

    /// <summary>
    /// Проверяет использование имени файла на диске и сохранение пустых необязательных значений.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task ReaderUsesDiskFileNameAndPreservesOptionalNullValues()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.WriteAsync("disk-name.m3d", """
            {"formatVersion":1,"fileName":"untrusted-name.m3d","type":"Part","designation":"АБВГ.301245.001","name":"Item","properties":{"material":null,"mass":null},"components":[]}
            """);
        await using var session = await fixture.OpenAsync();

        var result = await session.Reader.ReadAsync(new CadDocumentRefDto
        {
            FileName = "disk-name.m3d"
        }, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.Document);
        Assert.Equal("disk-name.m3d", result.Document.FileName);
        Assert.Null(result.Document.Material);
        Assert.Null(result.Document.Mass);
        Assert.Empty(result.Document.Components);
    }

    /// <summary>
    /// Проверяет возврат диагностик для документов с некорректным содержимым.
    /// </summary>
    /// <param name="contents">Содержимое документа или тестового файла.</param>
    /// <param name="expectedError">Ожидаемое сообщение диагностики.</param>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Theory]
    [InlineData("{", "invalid JSON")]
    [InlineData("{\"formatVersion\":2,\"type\":\"Part\",\"name\":\"x\",\"components\":[]}", "formatVersion")]
    [InlineData("{\"formatVersion\":1,\"type\":\"Part\",\"name\":\"x\",\"components\":[{\"file\":\"x.m3d\"}]}", "integer count")]
    [InlineData("{\"formatVersion\":1,\"type\":\"Part\",\"name\":\"x\",\"components\":[{\"file\":\"../x.m3d\",\"count\":1}]}", "integer count")]
    public async Task ReaderReturnsDiagnosticsForInvalidDocuments(string contents, string expectedError)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.WriteAsync("invalid.m3d", contents);
        await using var session = await fixture.OpenAsync();

        var result = await session.Reader.ReadAsync(new CadDocumentRefDto
        {
            FileName = "invalid.m3d"
        }, CancellationToken.None);

        Assert.Contains(expectedError, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Проверяет отклонение файла с несовпадающим расширением и отсутствующего файла.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task ReaderRejectsExtensionTypeMismatchAndMissingFile()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.WriteAsync("assembly.m3d", """
            {"formatVersion":1,"type":"Assembly","name":"Assembly","components":[]}
            """);
        await using var session = await fixture.OpenAsync();

        var mismatch = await session.Reader.ReadAsync(new CadDocumentRefDto
        {
            FileName = "assembly.m3d"
        }, CancellationToken.None);
        var missing = await session.Reader.ReadAsync(new CadDocumentRefDto
        {
            FileName = "missing.m3d"
        }, CancellationToken.None);

        Assert.Contains("does not match", mismatch.Error);
        Assert.Contains("not found", missing.Error);
    }

    /// <summary>
    /// Проверяет отклонение ссылки на файл, выходящей за пределы исходного каталога.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task ReaderRejectsPathTraversalReference()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var result = await session.Reader.ReadAsync(new CadDocumentRefDto
        {
            FileName = "../outside.m3d"
        }, CancellationToken.None);

        Assert.Contains("file name only", result.Error);
    }

    /// <summary>
    /// Проверяет передачу бизнес-некорректных имени и количества на основную валидацию.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task ReaderPreservesBusinessInvalidNameAndCountForCoreValidation()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.WriteAsync("invalid-values.m3d", """
            {"formatVersion":1,"type":"Part","name":"","components":[{"file":"component.m3d","count":-3}]}
            """);
        await using var session = await fixture.OpenAsync();

        var result = await session.Reader.ReadAsync(new CadDocumentRefDto
        {
            FileName = "invalid-values.m3d"
        }, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.Document);
        Assert.Equal(string.Empty, result.Document.Name);
        Assert.Equal(-3, result.Document.Components.Single().Count);
    }

    /// <summary>
    /// Проверяет сохранение идентичности документа при некорректной схеме компонента.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task ReaderPreservesIdentityOnInvalidComponentSchema()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.WriteAsync("invalid-count.m3d", """
            {"formatVersion":1,"type":"Part","designation":"АБВГ.301245.001","name":"Item","components":[{"file":"component.m3d","count":"one"}]}
            """);
        await using var session = await fixture.OpenAsync();

        var result = await session.Reader.ReadAsync(new CadDocumentRefDto
        {
            FileName = "invalid-count.m3d"
        }, CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.NotNull(result.Document);
        Assert.Equal("АБВГ.301245.001", result.Document.Designation);
        Assert.Equal("Item", result.Document.Name);
        Assert.Empty(result.Document.Components);
    }

    /// <summary>
    /// Проверяет чтение JSON-файла с меткой порядка байтов UTF-8.
    /// </summary>
    /// <returns>Задача завершается после выполнения проверок теста.</returns>
    [Fact]
    public async Task ReaderAcceptsUtf8ByteOrderMark()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.WriteUtf8BomAsync("bom.m3d", """
            {"formatVersion":1,"type":"Part","name":"Item","components":[]}
            """);
        await using var session = await fixture.OpenAsync();

        var result = await session.Reader.ReadAsync(new CadDocumentRefDto
        {
            FileName = "bom.m3d"
        }, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("Item", result.Document?.Name);
    }

    private sealed class Fixture(string directory) : IAsyncDisposable
    {
        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"cad-adapter-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            await Task.CompletedTask;
            return new Fixture(directory);
        }

        /// <summary>
        /// Записывает содержимое в тестовый CAD-файл с указанным именем.
        /// </summary>
        /// <param name="fileName">Имя файла тестового источника.</param>
        /// <param name="contents">Текст, записываемый в файл тестового источника.</param>
        /// <returns>Задача завершается после выполнения проверок теста.</returns>
        public Task WriteAsync(string fileName, string contents) => File.WriteAllTextAsync(Path.Combine(directory, fileName), contents);

        /// <summary>
        /// Записывает тестовый файл UTF-8 с меткой порядка байтов.
        /// </summary>
        /// <param name="fileName">Имя тестового файла.</param>
        /// <param name="contents">Содержимое документа или тестового файла.</param>
        /// <returns>Задача завершается после выполнения проверок теста.</returns>
        public Task WriteUtf8BomAsync(string fileName, string contents)
        {
            var encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            return File.WriteAllBytesAsync(Path.Combine(directory, fileName), encoding.GetPreamble().Concat(encoding.GetBytes(contents)).ToArray());
        }

        public Task<ICadSession> OpenAsync() => new FileJsonCadSourceAdapter().OpenAsync(
            new CadSourceDescriptorDto
            {
                Kind = FileJsonCadSourceAdapter.SourceKind,
                Location = directory
            }, CancellationToken.None);

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
