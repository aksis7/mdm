using MiniPdm.Contracts.Modules.BackgroundTasks.DtoModels;
using MiniPdm.Contracts.Modules.Calculations.DtoModels;
using MiniPdm.Contracts.Modules.Composition.DtoModels;
using MiniPdm.Contracts.Modules.Import.DtoModels;
using MiniPdm.Contracts.Modules.Objects.DtoModels;
using MiniPdm.Contracts.Modules.Versions.DtoModels;
using MiniPdm.Desktop.Services;
using MiniPdm.Desktop.Services.Abstractions;
using MiniPdm.Desktop.Modules.Import.ViewModels;
using MiniPdm.Desktop.Services.ImportFolderPickers;
using MiniPdm.Desktop.ViewModels;
using Xunit;

namespace MiniPdm.Desktop.Tests;

/// <summary>
/// Проверяет выбор объектов, изменение состава и обработку импорта в модели представления главного окна.
/// </summary>
public sealed class MainWindowViewModelTests
{
    /// <summary>
    /// Проверяет, что запоздавший ответ карточки не заменяет сведения о последнем выбранном объекте.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [Fact]
    public async Task LateObjectCardResponseCannotReplaceTheMostRecentSelection()
    {
        var client = new FakeClient();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.GetObjectOverride = async (id, version, ct) =>
        {
            if (id == firstId)
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task;
                return Card(firstId, "Old selection", token: Guid.NewGuid());
            }
            return Card(secondId, "New selection", token: Guid.NewGuid());
        };
        using var viewModel = new MainWindowViewModel(client);
        viewModel.SelectedObject = Item(firstId, "A");
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.SelectedObject = Item(secondId, "B");
        await WaitUntilAsync(() => viewModel.SelectedCard?.Id == secondId && !viewModel.IsBusy);
        releaseFirst.TrySetResult();
        await Task.Delay(30);

        Assert.Equal(secondId, viewModel.SelectedCard!.Id);
        Assert.Equal("New selection", viewModel.NameText);
    }

    /// <summary>
    /// Проверяет выбор исторической аннулированной версии и загрузку её состава, когда у объекта нет текущей версии.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [Fact]
    public async Task NoCurrentObjectSelectsHistoricalCancelledVersionAndLoadsItsComposition()
    {
        var client = new FakeClient();
        var objectId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var token = Guid.NewGuid();
        var historical = new ObjectVersionSummaryDto
        {
            Id = Guid.NewGuid(),
            Version = 3,
            State = "Cancelled",
            IsCurrent = false
        };
        client.Cards[objectId] = Card(objectId, null, token, historical, "Assembly");
        client.VersionCompositions[(objectId, 3)] = new VersionCompositionDto
        {
            ObjectId = objectId,
            Version = 3,
            ConcurrencyToken = token,
            Items = [new VersionCompositionItemDto
            {
                                ChildObjectId = childId,
                                Quantity = 4,
                                Type = "Part",
                                Designation = "АБВГ.301245.001",
                                Name = "Стойка",
                                NoCurrentVersion = true
            }

    ]
        };
        using var viewModel = new MainWindowViewModel(client);

        viewModel.SelectedObject = Item(objectId, "АБВГ.301245.002", "Assembly");
        await WaitUntilAsync(() => viewModel.SelectedVersion?.Version == 3 && !viewModel.IsBusy);

        Assert.Equal("Cancelled", viewModel.SelectedVersion!.State);
        Assert.Single(viewModel.Composition.Components);
        Assert.Equal(childId, viewModel.Composition.Components[0].ChildObjectId);
        Assert.Equal("4", viewModel.Composition.Components[0].QuantityText);
        Assert.True(viewModel.CloneCommand.CanExecute(null));
        Assert.False(viewModel.SaveCompositionCommand.CanExecute(null));
    }

    /// <summary>
    /// Проверяет сохранение введённых значений после конфликта до явного обновления оператором.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [Fact]
    public async Task ConflictPreservesEditorValuesUntilOperatorExplicitlyRefreshes()
    {
        var client = new FakeClient();
        var objectId = Guid.NewGuid();
        var firstToken = Guid.NewGuid();
        var latestToken = Guid.NewGuid();
        client.CardFactory = (id, requestedVersion, number) => Card(id,
            number == 1 ? "Server value" : "Server value after conflict",
            number == 1 ? firstToken : latestToken);
        client.ConflictOnUpdate = true;
        using var viewModel = new MainWindowViewModel(client);
        viewModel.SelectedObject = Item(objectId, "АБВГ.301245.010");
        await WaitUntilAsync(() => viewModel.SelectedVersion?.Version == 1 && !viewModel.IsBusy);
        viewModel.NameText = "Operator edit";

        await viewModel.SaveAttributesCommand.ExecuteAsync();

        Assert.Equal("Operator edit", viewModel.NameText);
        Assert.True(viewModel.IsReviewRequired);
        Assert.False(viewModel.SaveAttributesCommand.CanExecute(null));
        Assert.Equal(1, client.UpdateCount);

        await viewModel.RefreshSelectedCommand.ExecuteAsync();

        Assert.False(viewModel.IsReviewRequired);
        Assert.Equal("Server value after conflict", viewModel.NameText);
    }

    /// <summary>
    /// Проверяет повтор не подтверждённого импорта с прежним идентификатором и исходным содержимым файлов.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [Fact]
    public async Task UnconfirmedImportRetryReusesTheSameIdAndOriginalFilePayload()
    {
        var client = new FakeClient { ImportOutcomeUnknown = true };
        using var viewModel = new ImportViewModel(client, () => Task.CompletedTask);
        var paths = new[] { "/cad/one.a3d", "/cad/notes.txt", "/cad/two.M3D" };

        await viewModel.ImportFilesAsync(paths);
        var pendingId = viewModel.PendingImportId;
        Assert.NotNull(pendingId);
        Assert.Single(client.UploadIds);
        Assert.Equal(new[] { paths[0], paths[2] }, client.UploadedFiles[0]);

        client.ReportAvailable = true;
        await viewModel.RetryCommand.ExecuteAsync();

        Assert.Null(viewModel.PendingImportId);
        Assert.Single(client.UploadIds);
        Assert.Equal(pendingId, client.UploadIds[0]);
        Assert.Equal(2, client.ReportCount);
    }

    /// <summary>
    /// Проверяет сохранение аренды браузерного пакета при неопределённом результате и удаление файлов после подтверждённого повтора.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [Fact]
    public async Task BrowserPackageLeaseSurvivesUnknownOutcomeAndIsDeletedAfterConfirmedRetry()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mini-pdm-lease-{Guid.NewGuid():N}.a3d");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var client = new FakeClient { ImportOutcomeUnknown = true };
        using var viewModel = new ImportViewModel(client, () => Task.CompletedTask);
        var package = new SelectedImportPackage([path], new DeleteFileLease(path));

        await viewModel.ImportPackageAsync(package);

        Assert.True(File.Exists(path));
        Assert.NotNull(viewModel.PendingImportId);
        client.ReportAvailable = true;
        await viewModel.RetryCommand.ExecuteAsync();

        Assert.False(File.Exists(path));
        Assert.Null(viewModel.PendingImportId);
    }

    /// <summary>
    /// Ожидает выполнения условия или сообщает об истечении срока ожидания.
    /// </summary>
    /// <param name="condition">Условие завершения ожидания.</param>
    /// <returns>Завершение после выполнения условия либо ошибка утверждения по истечении пяти секунд.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < limit)
            await Task.Delay(10);
        Assert.True(condition());
    }

    /// <summary>
    /// Создаёт элемент результатов поиска для объекта с указанными идентификатором, обозначением и типом.
    /// </summary>
    /// <param name="id">Идентификатор объекта для чтения.</param>
    /// <param name="designation">Обозначение создаваемого объекта.</param>
    /// <param name="type">Тип создаваемого объекта.</param>
    /// <returns>Элемент поиска с указанными идентификатором, обозначением и типом объекта.</returns>
    private static ObjectSearchItemDto Item(Guid id, string designation, string type = "Part") =>
        new()
        {
            Id = id,
            Type = type,
            Designation = designation,
            Name = "Part",
            CurrentVersionId = null,
            VersionNumber = null,
            State = null,
            UnitMassKg = null,
            ConcurrencyToken = Guid.NewGuid(),
            NoCurrentVersion = true
        };

    /// <summary>
    /// Создаёт карточку объекта с текущей либо указанной исторической версией.
    /// </summary>
    /// <param name="id">Идентификатор объекта для чтения.</param>
    /// <param name="name">Наименование создаваемой версии.</param>
    /// <param name="token">Токен конкурентного доступа объекта.</param>
    /// <param name="history">Краткая запись об исторической версии объекта либо null.</param>
    /// <param name="type">Тип создаваемого объекта.</param>
    /// <returns>Карточку объекта с указанными сведениями и версиями.</returns>
    private static ObjectCardDto Card(Guid id, string? name, Guid token, ObjectVersionSummaryDto? history = null, string type = "Part")
    {
        var selected = history is null ? Version(1, "InWork", name ?? "Part") : null;
        IReadOnlyList<ObjectVersionSummaryDto> summaries = history is null
            ? [new ObjectVersionSummaryDto
            {
                                Id = selected!.Id,
                                Version = 1,
                                State = "InWork",
                                IsCurrent = true
            }] : [history];
        return new ObjectCardDto
        {
            Id = id,
            Type = type,
            Designation = "АБВГ.301245.001",
            Name = name,
            CurrentVersionId = null,
            ConcurrencyToken = token,
            SelectedVersion = selected,
            Versions = summaries,
            ErrorCode = null,
            Error = null
        };
    }

    /// <summary>
    /// Создаёт карточку объекта с текущей либо указанной исторической версией.
    /// </summary>
    /// <param name="id">Идентификатор объекта для чтения.</param>
    /// <param name="name">Наименование создаваемой версии.</param>
    /// <param name="token">Токен конкурентного доступа объекта.</param>
    /// <param name="selected">Версия, выбранная для карточки объекта, либо null.</param>
    /// <param name="summaries">Сводные сведения о версиях объекта.</param>
    /// <returns>Карточку объекта с указанными сведениями и версиями.</returns>
    private static ObjectCardDto Card(Guid id, string? name, Guid token, ObjectVersionDto? selected,
        IReadOnlyList<ObjectVersionSummaryDto> summaries) =>
        new()
        {
            Id = id,
            Type = "Part",
            Designation = "АБВГ.301245.001",
            Name = name,
            CurrentVersionId = null,
            ConcurrencyToken = token,
            SelectedVersion = selected,
            Versions = summaries,
            ErrorCode = null,
            Error = null
        };

    /// <summary>
    /// Создаёт версию с указанным номером, состоянием и наименованием.
    /// </summary>
    /// <param name="number">Номер создаваемой версии.</param>
    /// <param name="state">Состояние создаваемой версии.</param>
    /// <param name="name">Наименование создаваемой версии.</param>
    /// <returns>Версию объекта с новым идентификатором и заданными атрибутами.</returns>
    private static ObjectVersionDto Version(int number, string state, string? name)
    {
        var id = Guid.NewGuid();
        return new ObjectVersionDto
        {
            Id = id,
            Version = number,
            State = state,
            Name = name,
            Material = "Steel",
            UnitMassKg = 1m,
            SourceReference = null,
            IsCurrent = false
        };
    }

    private sealed class DeleteFileLease(string path) : IDisposable
    {
        /// <summary>
        /// Удаляет файл аренды, если он всё ещё существует.
        /// </summary>
        public void Dispose()
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private sealed class FakeClient : IPdmApiClient
    {
        private int _cardCalls;
        /// <summary>
        /// Карточки объектов, возвращаемые тестовым клиентом.
        /// </summary>
        public Dictionary<Guid, ObjectCardDto> Cards { get; } = [];

        /// <summary>
        /// Составы версий, возвращаемые тестовым клиентом.
        /// </summary>
        public Dictionary<(Guid, int), VersionCompositionDto> VersionCompositions { get; } = [];
        /// <summary>
        /// Фабрика карточек объектов для тестовых ответов.
        /// </summary>
        public Func<Guid, int?, int, ObjectCardDto>? CardFactory
        {
            get; set;
        }
        /// <summary>
        /// Переопределение получения карточки объекта в выбранном сценарии.
        /// </summary>
        public Func<Guid, int?, CancellationToken, Task<ObjectCardDto>>? GetObjectOverride
        {
            get; set;
        }
        /// <summary>
        /// Указывает, должен ли тестовый клиент возвращать конфликт обновления.
        /// </summary>
        public bool ConflictOnUpdate
        {
            get; set;
        }
        /// <summary>
        /// Указывает, должен ли результат импорта считаться неизвестным.
        /// </summary>
        public bool ImportOutcomeUnknown
        {
            get; set;
        }
        /// <summary>
        /// Указывает, доступен ли отчёт импорта при повторном запросе.
        /// </summary>
        public bool ReportAvailable
        {
            get; set;
        }
        /// <summary>
        /// Число вызовов обновления атрибутов.
        /// </summary>
        public int UpdateCount
        {
            get; private set;
        }
        /// <summary>
        /// Число запросов отчёта импорта.
        /// </summary>
        public int ReportCount
        {
            get; private set;
        }
        /// <summary>
        /// Идентификаторы пакетов, переданных при импорте.
        /// </summary>
        public List<Guid> UploadIds { get; } = [];
        /// <summary>
        /// Пути файлов, переданных тестовым клиенту.
        /// </summary>
        public List<string[]> UploadedFiles { get; } = [];

        /// <summary>
        /// Возвращает пустую страницу поиска с переданными смещением и размером страницы.
        /// </summary>
        /// <param name="search">Текстовый фильтр поиска; в этой фикстуре он не применяется.</param>
        /// <param name="offset">Число записей, пропускаемых перед страницей результатов.</param>
        /// <param name="limit">Максимальное число записей на странице результатов.</param>
        /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
        /// <returns>Пустую страницу результатов с сохранёнными значениями offset и limit.</returns>
        public Task<ObjectSearchPageDto> SearchObjectsAsync(string? search = null, int offset = 0, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ObjectSearchPageDto
            {
                Items = [],
                Offset = offset,
                Limit = limit,
                HasMore = false
            });
        /// <summary>
        /// Возвращает карточку из переопределения, фабрики или набора тестовых карточек; при отсутствии данных создаёт карточку по умолчанию.
        /// </summary>
        /// <param name="objectId">Идентификатор объекта, для которого запрашиваются данные.</param>
        /// <param name="version">Номер запрашиваемой версии объекта.</param>
        /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
        /// <returns>Карточку из переопределения, фабрики или словаря; либо карточку по умолчанию.</returns>
        public Task<ObjectCardDto> GetObjectAsync(Guid objectId, int? version = null, CancellationToken cancellationToken = default)
        {
            var number = Interlocked.Increment(ref _cardCalls);
            if (GetObjectOverride is not null)
                return GetObjectOverride(objectId, version, cancellationToken);
            if (CardFactory is not null)
            {
                var generated = CardFactory(objectId, version, number);
                Cards[objectId] = generated;
                return Task.FromResult(generated);
            }
            if (!Cards.TryGetValue(objectId, out var card))
                return Task.FromResult(Card(objectId, "Part", Guid.NewGuid()));
            if (version is null)
                return Task.FromResult(card);
            var selectedSummary = card.Versions.Single(x => x.Version == version);
            var selected = selectedSummary.Version == card.SelectedVersion?.Version
                ? card.SelectedVersion
                : new ObjectVersionDto
                {
                    Id = selectedSummary.Id,
                    Version = selectedSummary.Version,
                    State = selectedSummary.State,
                    Name = card.Type == "Assembly" ? null : "Restorable version",
                    Material = card.Type == "Assembly" ? null : "Steel",
                    UnitMassKg = card.Type == "Assembly" ? null : 1m,
                    SourceReference = null,
                    IsCurrent = selectedSummary.IsCurrent
                };
            return Task.FromResult(new ObjectCardDto
            {
                Id = card.Id,
                Type = card.Type,
                Designation = card.Designation,
                Name = card.Name,
                CurrentVersionId = card.CurrentVersionId,
                ConcurrencyToken = card.ConcurrencyToken,
                SelectedVersion = selected,
                Versions = card.Versions,
                ErrorCode = card.ErrorCode,
                Error = card.Error
            });
        }
        /// <summary>
        /// Возвращает пустое дерево состава для указанного корневого объекта.
        /// </summary>
        /// <param name="objectId">Идентификатор объекта, для которого запрашиваются данные.</param>
        /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
        /// <returns>Пустое дерево состава с указанным корневым объектом.</returns>
        public Task<CompositionTreeDto> GetCompositionAsync(Guid objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompositionTreeDto
            {
                RootObjectId = objectId,
                Nodes = []
            });
        /// <summary>
        /// Возвращает заданный для объекта и версии состав либо пустой состав с токеном известной карточки.
        /// </summary>
        /// <param name="objectId">Идентификатор объекта, для которого запрашиваются данные.</param>
        /// <param name="version">Номер запрашиваемой версии объекта.</param>
        /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
        /// <returns>Тестовый состав либо пустой состав с токеном известной карточки.</returns>
        public Task<VersionCompositionDto> GetVersionCompositionAsync(Guid objectId, int version, CancellationToken cancellationToken = default) =>
            Task.FromResult(VersionCompositions.GetValueOrDefault((objectId, version)) ?? new VersionCompositionDto
            {
                ObjectId = objectId,
                Version = version,
                ConcurrencyToken = Cards.GetValueOrDefault(objectId)?.ConcurrencyToken ?? Guid.Empty,
                Items = []
            });
        /// <summary>
        /// Возвращает неполный расчёт без строк спецификации и диагностик для указанного объекта.
        /// </summary>
        /// <param name="objectId">Идентификатор объекта, для которого запрашиваются данные.</param>
        /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
        /// <returns>Расчёт без известной массы, строк спецификации и диагностик.</returns>
        public Task<CompositionCalculationDto> GetCalculationAsync(Guid objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompositionCalculationDto
            {
                RootObjectId = objectId,
                TotalMassKg = null,
                IsComplete = false,
                Items = [],
                Diagnostics = []
            });
        /// <summary>
        /// Записывает идентификатор импорта и пути файлов; при неизвестном исходе выбрасывает ошибку 503, иначе возвращает пустой отчёт.
        /// </summary>
        /// <param name="importId">Идентификатор пакета импорта.</param>
        /// <param name="filePaths">Пути файлов, включённых в пакет импорта.</param>
        /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
        /// <returns>Пустой отчёт импорта при известном результате операции.</returns>
        public Task<ImportReportDto> ImportFilesAsync(Guid importId, IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default)
        {
            UploadIds.Add(importId);
            UploadedFiles.Add(filePaths.ToArray());
            if (ImportOutcomeUnknown)
                throw new PdmApiException(503, "Unknown upload outcome.");
            return Task.FromResult(new ImportReportDto
            {
                ImportId = importId,
                Files = []
            });
        }
        /// <summary>
        /// Увеличивает счётчик запросов отчёта; если отчёт недоступен, выбрасывает ошибку 404, иначе возвращает пустой отчёт.
        /// </summary>
        /// <param name="importId">Идентификатор пакета импорта.</param>
        /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
        /// <returns>Пустой отчёт с указанным идентификатором, если отчёт доступен.</returns>
        public Task<ImportReportDto> GetImportReportAsync(Guid importId, CancellationToken cancellationToken = default)
        {
            ReportCount++;
            if (!ReportAvailable)
                throw new PdmApiException(404, "Report not found.");
            return Task.FromResult(new ImportReportDto
            {
                ImportId = importId,
                Files = []
            });
        }
        /// <summary>
        /// Не поддерживает клонирование версии в этом тестовом клиенте и выбрасывает NotSupportedException.
        /// </summary>
        /// <param name="objectId">Идентификатор объекта, для которого запрашиваются данные.</param>
        /// <param name="request">Параметры клонирования исходной версии.</param>
        /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
        /// <returns>Задача не возвращается: вызов метода выбрасывает NotSupportedException, так как клонирование версии не поддерживается этим тестовым клиентом.</returns>
        public Task<VersionMutationDto> CloneVersionAsync(Guid objectId, CloneVersionRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <summary>
        /// Не поддерживает смену состояния версии в этом тестовом клиенте и выбрасывает NotSupportedException.
        /// </summary>
        /// <param name="objectId">Идентификатор объекта, для которого запрашиваются данные.</param>
        /// <param name="version">Номер запрашиваемой версии объекта.</param>
        /// <param name="request">Новое состояние и токен конкурентного доступа.</param>
        /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
        /// <returns>Задача не возвращается: вызов метода выбрасывает NotSupportedException, так как изменение состояния версии не поддерживается этим тестовым клиентом.</returns>
        public Task<VersionMutationDto> ChangeVersionStateAsync(Guid objectId, int version, ChangeVersionStateRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <summary>
        /// Увеличивает счётчик обновлений; при включённом флаге конфликта выбрасывает ошибку 409, иначе возвращает результат изменения версии.
        /// </summary>
        /// <param name="objectId">Идентификатор объекта, для которого запрашиваются данные.</param>
        /// <param name="version">Номер запрашиваемой версии объекта.</param>
        /// <param name="request">Новые атрибуты версии и токен конкурентного доступа.</param>
        /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
        /// <returns>Результат изменения версии с новым идентификатором и токеном конкурентного доступа.</returns>
        public Task<VersionMutationDto> UpdateVersionAttributesAsync(Guid objectId, int version, UpdateVersionAttributesRequestDto request, CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            if (ConflictOnUpdate)
                throw new PdmApiException(409, "The object was changed concurrently.");
            return Task.FromResult(new VersionMutationDto
            {
                ObjectId = objectId,
                VersionId = Guid.NewGuid(),
                VersionNumber = version,
                State = "InWork",
                CurrentVersionId = null,
                ConcurrencyToken = Guid.NewGuid(),
                Warnings = []
            });
        }
        /// <summary>
        /// Не поддерживает замену состава в этом тестовом клиенте и выбрасывает NotSupportedException.
        /// </summary>
        /// <param name="objectId">Идентификатор объекта, для которого запрашиваются данные.</param>
        /// <param name="version">Номер запрашиваемой версии объекта.</param>
        /// <param name="request">Новый список компонентов и токен конкурентного доступа.</param>
        /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
        /// <returns>Задача не возвращается: вызов метода выбрасывает NotSupportedException, так как замена состава не поддерживается этим тестовым клиентом.</returns>
        public Task<VersionMutationDto> ReplaceCompositionAsync(Guid objectId, int version, ReplaceCompositionRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <summary>
        /// Возвращает пустой список фоновых задач.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
        /// <returns>Пустой список фоновых задач.</returns>
        public Task<IReadOnlyList<BackgroundTaskDto>> GetBackgroundTasksAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BackgroundTaskDto>>([]);
        /// <summary>
        /// Не поддерживает изменение расписания в этом тестовом клиенте и выбрасывает NotSupportedException.
        /// </summary>
        /// <param name="taskId">Идентификатор фоновой задачи.</param>
        /// <param name="request">Новый интервал расписания задачи.</param>
        /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
        /// <returns>Задача не возвращается: вызов метода выбрасывает NotSupportedException, так как изменение расписания не поддерживается этим тестовым клиентом.</returns>
        public Task<BackgroundTaskDto> UpdateBackgroundTaskScheduleAsync(string taskId, UpdateBackgroundTaskScheduleRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <summary>
        /// Не поддерживает запуск фоновой задачи в этом тестовом клиенте и выбрасывает NotSupportedException.
        /// </summary>
        /// <param name="taskId">Идентификатор фоновой задачи.</param>
        /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
        /// <returns>Задача не возвращается: вызов метода выбрасывает NotSupportedException, так как запуск фоновой задачи не поддерживается этим тестовым клиентом.</returns>
        public Task<BackgroundTaskRunAcceptedDto> RunBackgroundTaskAsync(string taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
