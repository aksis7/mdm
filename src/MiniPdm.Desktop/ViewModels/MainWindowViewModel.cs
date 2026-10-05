using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using MiniPdm.Contracts.Modules.Composition.DtoModels;
using MiniPdm.Contracts.Modules.Objects.DtoModels;
using MiniPdm.Contracts.Modules.Versions.DtoModels;
using MiniPdm.Desktop.Services;
using MiniPdm.Desktop.Services.Abstractions;
using MiniPdm.Desktop.Modules.Import.ViewModels;
using MiniPdm.Desktop.Modules.BackgroundTasks.ViewModels;
using MiniPdm.Desktop.Modules.Composition.ViewModels;

namespace MiniPdm.Desktop.ViewModels;

/// <summary>
/// Управляет главным окном: каталогом объектов, карточками, составом, импортом и фоновыми задачами.
/// </summary>
public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IPdmApiClient _client;
    private readonly AsyncCommand _refreshCommand;
    private readonly AsyncCommand _loadMoreCommand;
    private readonly AsyncCommand _saveAttributesCommand;
    private readonly AsyncCommand _saveCompositionCommand;
    private readonly AsyncCommand _cloneCommand;
    private readonly AsyncCommand _approveCommand;
    private readonly AsyncCommand _cancelVersionCommand;
    private readonly AsyncCommand _searchChildrenCommand;
    private readonly AsyncCommand _refreshSelectedCommand;
    private ObjectSearchItemDto? _selectedObject;
    private ObjectCardDto? _selectedCard;
    private ObjectVersionSummaryDto? _selectedHistoryVersion;
    private string _searchText = string.Empty;
    private string _childSearchText = string.Empty;
    private string _nameText = string.Empty;
    private string _materialText = string.Empty;
    private string _massText = string.Empty;
    private string _statusText = "Подключение к серверу…";
    private string _messageText = string.Empty;
    private bool _isBusy;
    private int _offset;
    private bool _hasMore;
    private bool _isConnected;
    private CancellationTokenSource? _selectionCts;
    private bool _suppressHistoryLoad;
    private bool _suppressObjectSelectionLoad;
    private bool _requiresReview;
    private Guid? _compositionConcurrencyToken;

    /// <summary>
    /// Создаёт главную модель представления и загружает первую страницу каталога.
    /// </summary>
    /// <param name="client">Клиент API для работы с объектами и серверными операциями.</param>
    public MainWindowViewModel(IPdmApiClient client)
    {
        _client = client;
        BackgroundTasks = new BackgroundTasksViewModel(client);
        Import = new ImportViewModel(client, RefreshAfterImportAsync);
        Composition = new CompositionWorkspaceViewModel(SelectTreeObject);
        _refreshCommand = new AsyncCommand(() => RefreshAsync(resetOffset: true), () => !IsBusy, HandleCommandError);
        _loadMoreCommand = new AsyncCommand(LoadMoreAsync, () => !IsBusy && HasMore, HandleCommandError);
        _saveAttributesCommand = new AsyncCommand(SaveAttributesAsync, CanEditSelectedVersion, HandleCommandError);
        _saveCompositionCommand = new AsyncCommand(SaveCompositionAsync, CanEditComposition, HandleCommandError);
        _cloneCommand = new AsyncCommand(CloneAsync, () => !IsBusy && SelectedCard?.Versions.Count > 0, HandleCommandError);
        _approveCommand = new AsyncCommand(() => ChangeStateAsync("Approved"), () => !IsBusy && SelectedVersion?.State == "InWork", HandleCommandError);
        _cancelVersionCommand = new AsyncCommand(() => ChangeStateAsync("Cancelled"), () => !IsBusy && SelectedVersion?.State is "InWork" or "Approved", HandleCommandError);
        _searchChildrenCommand = new AsyncCommand(SearchChildrenAsync, () => !IsBusy, HandleCommandError);
        _refreshSelectedCommand = new AsyncCommand(LoadSelectedObjectAsync, () => !IsBusy && SelectedObject is not null, HandleCommandError);
        RefreshCommand = _refreshCommand;
        LoadMoreCommand = _loadMoreCommand;
        SaveAttributesCommand = _saveAttributesCommand;
        SaveCompositionCommand = _saveCompositionCommand;
        CloneCommand = _cloneCommand;
        ApproveCommand = _approveCommand;
        CancelVersionCommand = _cancelVersionCommand;
        SearchChildrenCommand = _searchChildrenCommand;
        RefreshSelectedCommand = _refreshSelectedCommand;
        _ = RefreshAsync(resetOffset: true);
    }

    /// <summary>
    /// Возникает при изменении свойства главной модели представления.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Возвращает загруженные строки каталога объектов.
    /// </summary>
    public ObservableCollection<ObjectSearchItemDto> Objects { get; } = [];

    /// <summary>
    /// Возвращает модель управления фоновыми задачами.
    /// </summary>
    public BackgroundTasksViewModel BackgroundTasks
    {
        get;
    }

    /// <summary>
    /// Возвращает модель управления импортом CAD-файлов.
    /// </summary>
    public ImportViewModel Import
    {
        get;
    }

    /// <summary>
    /// Возвращает модель дерева и редактируемого состава выбранного объекта.
    /// </summary>
    public CompositionWorkspaceViewModel Composition
    {
        get;
    }

    /// <summary>
    /// Возвращает команду обновления каталога с первой страницы.
    /// </summary>
    public AsyncCommand RefreshCommand
    {
        get;
    }

    /// <summary>
    /// Возвращает команду загрузки следующей страницы каталога.
    /// </summary>
    public AsyncCommand LoadMoreCommand
    {
        get;
    }

    /// <summary>
    /// Возвращает команду сохранения атрибутов версии.
    /// </summary>
    public AsyncCommand SaveAttributesCommand
    {
        get;
    }

    /// <summary>
    /// Возвращает команду сохранения состава версии.
    /// </summary>
    public AsyncCommand SaveCompositionCommand
    {
        get;
    }

    /// <summary>
    /// Возвращает команду создания версии на основе выбранной.
    /// </summary>
    public AsyncCommand CloneCommand
    {
        get;
    }

    /// <summary>
    /// Возвращает команду утверждения версии.
    /// </summary>
    public AsyncCommand ApproveCommand
    {
        get;
    }

    /// <summary>
    /// Возвращает команду аннулирования версии.
    /// </summary>
    public AsyncCommand CancelVersionCommand
    {
        get;
    }

    /// <summary>
    /// Возвращает команду поиска дочерних объектов для состава.
    /// </summary>
    public AsyncCommand SearchChildrenCommand
    {
        get;
    }

    /// <summary>
    /// Возвращает команду обновления выбранной карточки.
    /// </summary>
    public AsyncCommand RefreshSelectedCommand
    {
        get;
    }

    /// <summary>
    /// Получает или задаёт строку поиска объектов в каталоге.
    /// </summary>
    public string SearchText
    {
        get => _searchText; set => Set(ref _searchText, value);
    }

    /// <summary>
    /// Получает или задаёт строку поиска дочерних объектов для состава.
    /// </summary>
    public string ChildSearchText
    {
        get => _childSearchText; set => Set(ref _childSearchText, value);
    }

    /// <summary>
    /// Получает или задаёт редактируемое имя версии.
    /// </summary>
    public string NameText
    {
        get => _nameText; set => Set(ref _nameText, value);
    }

    /// <summary>
    /// Получает или задаёт редактируемый материал версии.
    /// </summary>
    public string MaterialText
    {
        get => _materialText; set => Set(ref _materialText, value);
    }

    /// <summary>
    /// Получает или задаёт текст удельной массы версии.
    /// </summary>
    public string MassText
    {
        get => _massText; set => Set(ref _massText, value);
    }

    /// <summary>
    /// Возвращает состояние подключения и загрузки каталога.
    /// </summary>
    public string StatusText
    {
        get => _statusText; private set => Set(ref _statusText, value);
    }

    /// <summary>
    /// Возвращает сообщение о текущем действии или результате изменения.
    /// </summary>
    public string MessageText
    {
        get => _messageText; private set => Set(ref _messageText, value);
    }

    /// <summary>
    /// Показывает, выполняется ли операция главного окна.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy; private set
        {
            if (Set(ref _isBusy, value))
                RefreshCommands();
        }
    }

    /// <summary>
    /// Показывает, доступен ли сервер API.
    /// </summary>
    public bool IsConnected
    {
        get => _isConnected; private set => Set(ref _isConnected, value);
    }

    /// <summary>
    /// Показывает, содержит ли каталог следующую страницу.
    /// </summary>
    public bool HasMore
    {
        get => _hasMore; private set
        {
            if (Set(ref _hasMore, value))
                _loadMoreCommand.Refresh();
        }
    }

    /// <summary>
    /// Возвращает подпись выбранного объекта.
    /// </summary>
    public string? SelectedObjectLabel => SelectedObject is null ? null : ObjectLabel(SelectedObject);

    /// <summary>
    /// Возвращает имя выбранного объекта.
    /// </summary>
    public string? SelectedObjectName => SelectedObject?.Name;

    /// <summary>
    /// Возвращает тип выбранной карточки.
    /// </summary>
    public string? SelectedCardType => SelectedCard?.Type;

    /// <summary>
    /// Возвращает историю версий выбранной карточки.
    /// </summary>
    public IReadOnlyList<ObjectVersionSummaryDto> HistoryVersions => SelectedCard?.Versions ?? Array.Empty<ObjectVersionSummaryDto>();

    /// <summary>
    /// Возвращает подпись выбранной версии и её состояния.
    /// </summary>
    public string? SelectedVersionLabel => SelectedVersion is null ? null : $"Версия {SelectedVersion.Version} — {StateLabel(SelectedVersion.State)}";

    /// <summary>
    /// Возвращает подпись текущей действующей версии карточки.
    /// </summary>
    public string CurrentVersionLabel => SelectedCard?.Versions.FirstOrDefault(x => x.IsCurrent) is { } current
        ? $"Текущая версия: {current.Version} — {StateLabel(current.State)}" : "Текущей действующей версии нет";

    /// <summary>
    /// Возвращает выбранную версию карточки.
    /// </summary>
    public ObjectVersionDto? SelectedVersion => SelectedCard?.SelectedVersion;

    /// <summary>
    /// Показывает, является ли выбранный объект стандартным изделием.
    /// </summary>
    public bool IsStandardPart => SelectedObject?.Type == "StandardPart";

    /// <summary>
    /// Показывает, является ли выбранный объект сборкой.
    /// </summary>
    public bool IsAssembly => SelectedObject?.Type == "Assembly";

    /// <summary>
    /// Показывает, находится ли выбранная версия в состоянии редактирования.
    /// </summary>
    public bool IsEditable => SelectedVersion?.State == "InWork";

    /// <summary>
    /// Показывает, разрешено ли редактирование состава.
    /// </summary>
    public bool CanEditCompositionUi => CanEditComposition();

    /// <summary>
    /// Показывает, разрешено ли редактирование атрибутов версии.
    /// </summary>
    public bool CanEditAttributes => IsEditable && !IsBusy && !IsReviewRequired;

    /// <summary>
    /// Показывает, доступно ли редактирование имени выбранного объекта.
    /// </summary>
    public bool CanEditName => SelectedCard?.SelectedVersion is not null && !IsStandardPart;

    /// <summary>
    /// Показывает, доступно ли редактирование материала выбранной детали.
    /// </summary>
    public bool CanEditMaterial => SelectedCard?.SelectedVersion is not null && SelectedObject?.Type == "Part";

    /// <summary>
    /// Показывает, доступно ли редактирование массы выбранной детали.
    /// </summary>
    public bool CanEditMass => SelectedCard?.SelectedVersion is not null && (SelectedObject?.Type is "Part" or "StandardPart");

    /// <summary>
    /// Показывает, можно ли утвердить выбранную версию.
    /// </summary>
    public bool CanApprove => SelectedVersion?.State == "InWork";

    /// <summary>
    /// Показывает, можно ли аннулировать выбранную версию.
    /// </summary>
    public bool CanCancel => SelectedVersion?.State is "InWork" or "Approved";

    /// <summary>
    /// Возвращает ссылку на исходный файл выбранной версии.
    /// </summary>
    public string? SelectedSourceReference => SelectedVersion?.SourceReference;

    /// <summary>
    /// Показывает, требуется ли проверить карточку после конфликта или неопределённого результата.
    /// </summary>
    public bool IsReviewRequired
    {
        get => _requiresReview; private set
        {
            if (Set(ref _requiresReview, value))
                RefreshCommands();
        }
    }

    /// <summary>
    /// Получает или задаёт выбранную строку каталога и загружает её карточку.
    /// </summary>
    public ObjectSearchItemDto? SelectedObject
    {
        get => _selectedObject;
        set
        {
            if (!Set(ref _selectedObject, value))
                return;
            Notify(nameof(SelectedObjectLabel));
            Notify(nameof(SelectedObjectName));
            if (_suppressObjectSelectionLoad)
                return;
            IsReviewRequired = false;
            Composition.CanEdit = false;
            _ = LoadSelectedObjectAsync();
        }
    }

    /// <summary>
    /// Возвращает загруженную карточку выбранного объекта.
    /// </summary>
    public ObjectCardDto? SelectedCard
    {
        get => _selectedCard; private set
        {
            if (Set(ref _selectedCard, value))
                NotifyCardProperties();
        }
    }

    /// <summary>
    /// Получает или задаёт выбранную запись истории и загружает соответствующую версию.
    /// </summary>
    public ObjectVersionSummaryDto? SelectedHistoryVersion
    {
        get => _selectedHistoryVersion;
        set
        {
            if (_suppressHistoryLoad)
            {
                Set(ref _selectedHistoryVersion, value);
                return;
            }
            if (IsBusy || !Set(ref _selectedHistoryVersion, value))
                return;
            Notify(nameof(SelectedVersionLabel));
            if (SelectedObject is not null && value is not null)
                _ = LoadVersionAsync(value.Version);
        }
    }

    /// <summary>
    /// Импортирует файлы из заданного списка путей.
    /// </summary>
    /// <param name="filePaths">Пути к CAD-файлам.</param>
    /// <returns>Асинхронная операция импорта.</returns>
    public Task ImportFolderAsync(IReadOnlyList<string> filePaths) => Import.ImportFilesAsync(filePaths);

    /// <summary>
    /// Показывает сообщение об ошибке, возникшей при действии окна.
    /// </summary>
    /// <param name="message">Текст ошибки для отображения.</param>
    public void ReportError(string message) => MessageText = message;

    private async Task RefreshAfterImportAsync()
    {
        if (IsBusy)
        {
            MessageText = "Импорт подтверждён. Обновите список после завершения текущей операции.";
            return;
        }
        await RefreshAsync(resetOffset: true);
    }

    private async Task RefreshAsync(bool resetOffset, bool insideBusy = false)
    {
        if (!insideBusy)
            await RunBusyAsync(() => RefreshAsync(resetOffset, insideBusy: true));
        else
        {
            var selectedObjectId = resetOffset ? SelectedObject?.Id : null;
            try
            {
                var page = await _client.SearchObjectsAsync(SearchText, resetOffset ? 0 : _offset, 50);
                if (resetOffset)
                {
                    _suppressObjectSelectionLoad = true;
                    try
                    {
                        Objects.Clear();
                        if (selectedObjectId.HasValue)
                            SelectedObject = null;
                    }
                    finally { _suppressObjectSelectionLoad = false; }
                }
                foreach (var item in page.Items)
                    Objects.Add(item);
                _offset = page.Offset + page.Items.Count;
                HasMore = page.HasMore;
                IsConnected = true;
                StatusText = $"Сервер доступен · объектов на экране: {Objects.Count}";
                if (selectedObjectId is Guid objectId)
                {
                    var updated = Objects.FirstOrDefault(x => x.Id == objectId);
                    if (updated is not null)
                        SelectedObject = updated;
                    else
                    {
                        SelectedObject = null;
                        await LoadSelectedObjectAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                IsConnected = false;
                StatusText = $"Нет связи с сервером: {ex.Message}";
            }
        }
    }

    private async Task LoadMoreAsync() => await RunBusyAsync(async () =>
    {
        var page = await _client.SearchObjectsAsync(SearchText, _offset, 50);
        foreach (var item in page.Items)
            Objects.Add(item);
        _offset = page.Offset + page.Items.Count;
        HasMore = page.HasMore;
    });

    private async Task LoadSelectedObjectAsync()
    {
        if (SelectedObject is null)
        {
            SelectedCard = null;
            return;
        }
        var objectId = SelectedObject.Id;
        IsReviewRequired = false;
        _selectionCts?.Cancel();
        _selectionCts?.Dispose();
        var cts = _selectionCts = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            var card = await _client.GetObjectAsync(objectId, cancellationToken: cts.Token);
            if (!IsCurrentSelection(objectId, cts))
                return;
            if (card.SelectedVersion is null && card.Versions.Count > 0)
            {
                var fallback = card.Versions.OrderByDescending(x => x.Version).First();
                card = await _client.GetObjectAsync(objectId, fallback.Version, cts.Token);
            }
            var loaded = await LoadConsistentCardCompositionAsync(objectId, card, cts.Token);
            card = loaded.Card;
            if (!IsCurrentSelection(objectId, cts))
                return;
            ApplyCard(card, updateEditor: true);
            ApplyEditableComposition(loaded.Composition);
            await LoadPanelsAsync(objectId, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) { if (IsCurrentSelection(objectId, cts)) MessageText = $"Не удалось открыть объект: {ex.Message}"; }
        finally
        {
            if (ReferenceEquals(_selectionCts, cts))
                IsBusy = false;
        }
    }

    private async Task LoadVersionAsync(int version)
    {
        if (SelectedObject is null)
            return;
        var objectId = SelectedObject.Id;
        IsReviewRequired = false;
        _selectionCts?.Cancel();
        _selectionCts?.Dispose();
        var cts = _selectionCts = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            var card = await _client.GetObjectAsync(objectId, version, cts.Token);
            if (!IsCurrentSelection(objectId, cts))
                return;
            var loaded = await LoadConsistentCardCompositionAsync(objectId, card, cts.Token);
            card = loaded.Card;
            if (!IsCurrentSelection(objectId, cts))
                return;
            ApplyCard(card, updateEditor: true);
            ApplyEditableComposition(loaded.Composition);
            await LoadPanelsAsync(objectId, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) { if (IsCurrentSelection(objectId, cts)) MessageText = $"Не удалось открыть версию: {ex.Message}"; }
        finally
        {
            if (ReferenceEquals(_selectionCts, cts))
                IsBusy = false;
        }
    }

    private async Task<(ObjectCardDto Card, VersionCompositionDto? Composition)> LoadConsistentCardCompositionAsync(
        Guid objectId, ObjectCardDto initialCard, CancellationToken cancellationToken)
    {
        var card = initialCard;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            VersionCompositionDto? composition = null;
            if (card.Type == "Assembly" && card.SelectedVersion is not null)
            {
                composition = await _client.GetVersionCompositionAsync(objectId, card.SelectedVersion.Version, cancellationToken);
                if (composition.ConcurrencyToken != card.ConcurrencyToken)
                {
                    card = await _client.GetObjectAsync(objectId, card.SelectedVersion.Version, cancellationToken);
                    continue;
                }
            }
            return (card, composition);
        }
        throw new InvalidOperationException("Карточка и состав изменились во время чтения. Обновите объект и повторите просмотр.");
    }

    private bool IsCurrentSelection(Guid objectId, CancellationTokenSource cts) =>
        !cts.IsCancellationRequested && ReferenceEquals(_selectionCts, cts) && SelectedObject?.Id == objectId;

    private void ApplyEditableComposition(VersionCompositionDto? composition)
    {
        _compositionConcurrencyToken = composition?.ConcurrencyToken;
        Composition.SetItems(composition?.Items ?? [], id =>
        {
            var obj = Objects.FirstOrDefault(x => x.Id == id);
            var source = composition?.Items.FirstOrDefault(x => x.ChildObjectId == id);
            return obj is null ? source?.Designation ?? source?.Name ?? id.ToString() : ObjectLabel(obj);
        });
        Composition.CanEdit = CanEditComposition();
        Notify(nameof(CanEditCompositionUi));
    }

    private async Task LoadPanelsAsync(Guid objectId, CancellationToken cancellationToken = default)
    {
        try
        {
            var tree = await _client.GetCompositionAsync(objectId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (SelectedObject?.Id != objectId)
                return;
            Composition.SetTree(tree);
            var calculation = await _client.GetCalculationAsync(objectId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (SelectedObject?.Id != objectId)
                return;
            Composition.SetCalculation(calculation);
        }
        catch (PdmApiException ex) when (ex.StatusCode == 404)
        {
            Composition.ClearCurrent();
        }
    }

    private void ApplyCard(ObjectCardDto card, bool updateEditor)
    {
        SelectedCard = card;
        _suppressHistoryLoad = true;
        SelectedHistoryVersion = card.Versions.FirstOrDefault(x => x.Version == card.SelectedVersion?.Version)
            ?? (card.SelectedVersion is null ? card.Versions.OrderByDescending(x => x.Version).FirstOrDefault() : null);
        _suppressHistoryLoad = false;
        Notify(nameof(SelectedSourceReference));
        if (updateEditor)
        {
            NameText = card.SelectedVersion?.Name ?? string.Empty;
            MaterialText = card.SelectedVersion?.Material ?? string.Empty;
            MassText = card.SelectedVersion?.UnitMassKg?.ToString("0.######", CultureInfo.GetCultureInfo("ru-RU")) ?? string.Empty;
        }
    }

    private async Task SearchChildrenAsync() => await RunBusyAsync(async () =>
    {
        var page = await _client.SearchObjectsAsync(ChildSearchText, 0, 30);
        Composition.ChildResults.Clear();
        foreach (var item in page.Items)
            Composition.ChildResults.Add(item);
    });

    private async Task SaveAttributesAsync()
    {
        if (SelectedObject is null || SelectedVersion is null || SelectedCard is null)
            return;
        decimal? mass = null;
        if (MassText.Length != 0)
        {
            const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
            var ru = CultureInfo.GetCultureInfo("ru-RU");
            if (!decimal.TryParse(MassText, styles, CultureInfo.InvariantCulture, out var parsed)
                && !decimal.TryParse(MassText, styles, ru, out parsed))
            {
                MessageText = "Введите массу как десятичное число без округления, например 1,25.";
                return;
            }
            mass = parsed;
        }
        if (IsAssembly)
            mass = null;
        var objectId = SelectedObject.Id;
        var version = SelectedVersion.Version;
        var name = IsStandardPart ? SelectedObject.Name : NameText;
        var material = IsAssembly || IsStandardPart ? null : MaterialText;
        await ExecuteMutationAsync(token => _client.UpdateVersionAttributesAsync(objectId,
            version, new UpdateVersionAttributesRequestDto
            {
                Name = name,
                Material = material,
                Mass = mass,
                ExpectedConcurrencyToken = token
            }));
    }

    private async Task SaveCompositionAsync()
    {
        if (SelectedObject is null || SelectedVersion is null || SelectedCard is null || _compositionConcurrencyToken != SelectedCard.ConcurrencyToken)
            return;
        var components = new List<CompositionItemDto> { };
        foreach (var item in Composition.Components)
        {
            if (!item.TryGetPositiveQuantity(out var quantity))
            {
                MessageText = $"Укажите для компонента «{item.Label}» положительное целое количество в диапазоне Int32.";
                return;
            }
            components.Add(new CompositionItemDto
            {
                ChildObjectId = item.ChildObjectId,
                Quantity = quantity
            });
        }
        var objectId = SelectedObject.Id;
        var version = SelectedVersion.Version;
        var concurrencyToken = _compositionConcurrencyToken.Value;
        await ExecuteMutationAsync(_ => _client.ReplaceCompositionAsync(objectId,
            version, new ReplaceCompositionRequestDto
            {
                Components = components,
                ExpectedConcurrencyToken = concurrencyToken
            }));
    }

    private async Task CloneAsync()
    {
        if (SelectedObject is null || SelectedVersion is null || SelectedCard is null)
            return;
        var objectId = SelectedObject.Id;
        var version = SelectedVersion.Version;
        await ExecuteMutationAsync(token => _client.CloneVersionAsync(objectId,
            new CloneVersionRequestDto
            {
                SourceVersion = version,
                ExpectedConcurrencyToken = token
            }));
    }

    private async Task ChangeStateAsync(string state)
    {
        if (SelectedObject is null || SelectedVersion is null || SelectedCard is null)
            return;
        var objectId = SelectedObject.Id;
        var version = SelectedVersion.Version;
        await ExecuteMutationAsync(token => _client.ChangeVersionStateAsync(objectId,
            version, new ChangeVersionStateRequestDto
            {
                State = state,
                ExpectedConcurrencyToken = token
            }));
    }

    private async Task ExecuteMutationAsync(Func<Guid, Task<VersionMutationDto>> operation)
    {
        if (SelectedObject is null || SelectedCard is null)
            return;
        var objectId = SelectedObject.Id;
        var token = SelectedCard.ConcurrencyToken;
        if (IsReviewRequired)
        {
            MessageText = "Обновите карточку и проверьте изменения перед сохранением.";
            return;
        }
        await RunBusyAsync(async () =>
        {
            try
            {
                var result = await operation(token);
                IsReviewRequired = false;
                MessageText = result.Warnings.Count == 0 ? "Изменение сохранено." : string.Join(" ", result.Warnings);
                await RefreshCardAfterMutationAsync(objectId, result.VersionNumber);
            }
            catch (PdmApiException ex) when (ex.StatusCode == 409)
            {
                MessageText = $"Конфликт версии: {ex.Message} Редактор сохранён; данные карточки обновлены. Проверьте изменения и сохраните повторно.";
                IsReviewRequired = true;
                await RefreshCardAfterMutationAsync(objectId, SelectedVersion?.Version, updateEditor: false, refreshComposition: false);
            }
            catch (PdmApiException ex) when (ex.IsOutcomeUnknown)
            {
                MessageText = $"Сервер не подтвердил результат. Карточка обновлена; проверьте её перед повторной командой. {ex.Message}";
                IsReviewRequired = true;
                await RefreshCardAfterMutationAsync(objectId, SelectedVersion?.Version, updateEditor: false, refreshComposition: false);
            }
            catch (Exception ex) { MessageText = ex.Message; }
        });
    }

    private async Task RefreshCardAfterMutationAsync(Guid objectId, int? version, bool updateEditor = true, bool refreshComposition = true)
    {
        var card = await _client.GetObjectAsync(objectId, version);
        ApplyCard(card, updateEditor);
        if (SelectedObject is not null)
        {
            var refreshed = await _client.SearchObjectsAsync(SearchText, 0, 100);
            var row = refreshed.Items.FirstOrDefault(x => x.Id == objectId);
            if (row is not null)
            {
                var oldRow = Objects.FirstOrDefault(x => x.Id == objectId);
                if (oldRow is not null)
                {
                    var index = Objects.IndexOf(oldRow);
                    if (index >= 0)
                    {
                        var preserveSelection = SelectedObject?.Id == objectId;
                        _suppressObjectSelectionLoad = true;
                        try
                        {
                            Objects[index] = row;
                            if (preserveSelection)
                                SelectedObject = row;
                        }
                        finally { _suppressObjectSelectionLoad = false; }
                    }
                }
            }
        }
        if (refreshComposition)
        {
            var loaded = await LoadConsistentCardCompositionAsync(objectId, card, default);
            ApplyCard(loaded.Card, updateEditor);
            if (updateEditor)
                ApplyEditableComposition(loaded.Composition);
            await LoadPanelsAsync(objectId);
        }
    }

    private bool CanEditSelectedVersion() => !IsBusy && !IsReviewRequired && IsEditable && SelectedCard is not null;
    private bool CanEditComposition() => !IsBusy && !IsReviewRequired && IsAssembly && IsEditable && SelectedCard is not null
        && _compositionConcurrencyToken == SelectedCard.ConcurrencyToken;

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            await action();
        }
        finally { IsBusy = false; }
    }

    private void RefreshCommands()
    {
        _refreshCommand.Refresh();
        _loadMoreCommand.Refresh();
        _saveAttributesCommand.Refresh();
        _saveCompositionCommand.Refresh();
        _cloneCommand.Refresh();
        _approveCommand.Refresh();
        _cancelVersionCommand.Refresh();
        _searchChildrenCommand.Refresh();
        _refreshSelectedCommand.Refresh();
        Composition.CanEdit = CanEditComposition();
        Notify(nameof(CanEditCompositionUi));
        Notify(nameof(IsEditable));
        Notify(nameof(CanApprove));
        Notify(nameof(CanCancel));
        Notify(nameof(CanEditAttributes));
        Notify(nameof(CanEditName));
        Notify(nameof(CanEditMaterial));
        Notify(nameof(CanEditMass));
    }

    private void NotifyCardProperties()
    {
        Notify(nameof(SelectedCardType));
        Notify(nameof(HistoryVersions));
        Notify(nameof(SelectedVersion));
        Notify(nameof(SelectedVersionLabel));
        Notify(nameof(IsStandardPart));
        Notify(nameof(IsAssembly));
        Notify(nameof(IsEditable));
        Notify(nameof(CanEditAttributes));
        Notify(nameof(CanEditName));
        Notify(nameof(CanEditMaterial));
        Notify(nameof(CanEditMass));
        Notify(nameof(CurrentVersionLabel));
        _saveAttributesCommand.Refresh();
        _saveCompositionCommand.Refresh();
        _cloneCommand.Refresh();
        _approveCommand.Refresh();
        _cancelVersionCommand.Refresh();
        Composition.CanEdit = CanEditComposition();
        Notify(nameof(CanEditCompositionUi));
    }

    private void HandleCommandError(Exception exception) => MessageText = exception.Message;
    private void SelectTreeObject(Guid objectId)
    {
        if (IsBusy)
            return;
        var node = Composition.TreeRoots.SelectMany(FlattenTree).FirstOrDefault(x => x.Node.ObjectId == objectId);
        if (node is null)
            return;
        var item = Objects.FirstOrDefault(x => x.Id == objectId);
        if (item is null)
        {
            item = new ObjectSearchItemDto
            {
                Id = node.Node.ObjectId,
                Type = node.Node.Type,
                Designation = node.Node.Designation,
                Name = node.Node.Name,
                CurrentVersionId = node.Node.VersionId,
                VersionNumber = node.Node.VersionNumber,
                State = node.Node.State,
                UnitMassKg = node.Node.UnitMassKg,
                ConcurrencyToken = Guid.Empty,
                NoCurrentVersion = node.Node.VersionId is null
            };
            Objects.Insert(0, item);
        }
        SelectedObject = item;
    }
    private static IEnumerable<TreeOccurrenceViewModel> FlattenTree(TreeOccurrenceViewModel root)
    {
        yield return root;
        foreach (var child in root.Children.SelectMany(FlattenTree))
            yield return child;
    }
    private static string ObjectLabel(ObjectSearchItemDto item) => item.Type switch
    {
        "StandardPart" => item.Name ?? "Стандартное изделие",
        _ => $"{item.Designation ?? "Без обозначения"} — {item.Name ?? item.Type}"
    };
    private static string StateLabel(string state) => state switch { "InWork" => "В работе", "Approved" => "Утверждено", "Cancelled" => "Аннулировано", _ => state };

    /// <summary>
    /// Отменяет загрузку карточки и освобождает дочерние модели представления.
    /// </summary>
    public void Dispose()
    {
        _selectionCts?.Cancel();
        _selectionCts?.Dispose();
        _selectionCts = null;
        BackgroundTasks.Dispose();
        Import.Dispose();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        Notify(name);
        return true;
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
