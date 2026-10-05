using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Logging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MiniPdm.Contracts.Modules.BackgroundTasks.DtoModels;
using MiniPdm.Contracts.Modules.Calculations.DtoModels;
using MiniPdm.Contracts.Modules.Composition.DtoModels;
using MiniPdm.Contracts.Modules.Import.DtoModels;
using MiniPdm.Contracts.Modules.Objects.DtoModels;
using MiniPdm.Contracts.Modules.Versions.DtoModels;
using MiniPdm.Desktop;
using MiniPdm.Desktop.Services;
using MiniPdm.Desktop.Services.ImportFolderPickers;
using MiniPdm.Desktop.Modules.Composition.Views;
using MiniPdm.Desktop.Modules.Import.Views;
using MiniPdm.Desktop.ViewModels;
using Xunit;

namespace MiniPdm.Desktop.Tests;

/// <summary>
/// Проверяет поведение главного окна, отображение данных и взаимодействие с выбором файлов.
/// </summary>
public sealed class DesktopWindowTests
{
    private static readonly Guid AssemblyId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid HistoricalChildId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid CurrentChildId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid CurrentVersionId = Guid.Parse("40000000-0000-0000-0000-000000000004");
    private static readonly Guid HistoricalVersionId = Guid.Parse("50000000-0000-0000-0000-000000000005");
    private static readonly Guid ConcurrencyToken = Guid.Parse("60000000-0000-0000-0000-000000000006");

    /// <summary>
    /// Проверяет, что содержимое вкладки отчёта импорта использует соответствующую модель представления.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [AvaloniaFact]
    public async Task ImportReportContentUsesImportViewModelAsDataContext()
    {
        var handler = new DesktopApiHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        using var viewModel = new MainWindowViewModel(new PdmApiClient(httpClient));
        var window = new MainWindow { DataContext = viewModel, Width = 1480, Height = 920 };

        try
        {
            window.Show();
            await WaitUntilAsync(() => viewModel.Objects.Count == 3 && !viewModel.IsBusy,
                "Object search did not finish in time.");
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
            var reportTab = window.FindControl<TabItem>("ImportReportTab")!;
            tabs.SelectedItem = reportTab;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var reportView = window.FindControl<ImportReportView>("ImportReportView")!;
            Assert.Same(viewModel.Import, reportView.DataContext);
            Assert.Same(viewModel.Import, reportView.FindControl<Grid>("ImportReportContent")!.DataContext);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Проверяет восстановление кнопки импорта и сброс состояния выбора после отмены диалога.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [AvaloniaFact]
    public async Task CancelingFolderPickerRestoresImportButtonAndClearsPickingState()
    {
        var handler = new DesktopApiHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        using var viewModel = new MainWindowViewModel(new PdmApiClient(httpClient));
        var picker = new ControlledFolderPicker();
        var window = new MainWindow { DataContext = viewModel, FolderPicker = picker, Width = 1480, Height = 920 };

        try
        {
            window.Show();
            await WaitUntilAsync(() => viewModel.Objects.Count == 3 && !viewModel.IsBusy,
                "Object search did not finish in time.");
            var importButton = window.FindControl<Button>("ImportFolderButton")!;
            Assert.True(importButton.IsEnabled);

            importButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await picker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(importButton.IsEnabled);
            Assert.True(GetIsPicking(window));

            picker.Complete(null);
            await WaitUntilAsync(() => !GetIsPicking(window) && importButton.IsEnabled,
                "Folder picker cancellation did not restore the import button.");

            Assert.False(viewModel.Import.IsBusy);
            Assert.Null(viewModel.Import.PendingImportId);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Проверяет восстановление кнопки, отображение отчёта и сохранение выбора после повторных импортов.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [AvaloniaFact]
    public async Task SuccessfulRepeatedImportsRestoreButtonAndRenderEachReport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mini-pdm-{Guid.NewGuid():N}.a3d");
        await File.WriteAllTextAsync(path, "{}");
        var handler = new DesktopApiHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        using var viewModel = new MainWindowViewModel(new PdmApiClient(httpClient));
        var picker = new RepeatingFolderPicker(path);
        var window = new MainWindow { DataContext = viewModel, FolderPicker = picker, Width = 1480, Height = 920 };

        try
        {
            window.Show();
            await WaitUntilAsync(() => viewModel.Objects.Count == 3 && !viewModel.IsBusy,
                "Object search did not finish in time.");
            var importButton = window.FindControl<Button>("ImportFolderButton")!;
            var objectList = window.GetVisualDescendants().OfType<ListBox>()
                .Single(list => ReferenceEquals(list.ItemsSource, viewModel.Objects));
            objectList.SelectedItem = viewModel.Objects.Single(item => item.Id == AssemblyId);
            await WaitUntilAsync(() => viewModel.SelectedObject?.Id == AssemblyId
                && viewModel.SelectedCard?.Id == AssemblyId && !viewModel.IsBusy,
                "The selected object did not finish loading before import.");

            for (var importNumber = 1; importNumber <= 2; importNumber++)
            {
                Assert.True(importButton.IsEnabled);
                importButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntilAsync(() => picker.CallCount == importNumber && !GetIsPicking(window)
                    && !viewModel.Import.IsBusy && viewModel.Import.PendingImportId is null
                    && viewModel.Import.Files.Count == 1 && importButton.IsEnabled,
                    $"Import {importNumber} did not finish and restore the import button.");

                Assert.Equal(importNumber, handler.ImportIds.Count);
                Assert.Single(viewModel.Import.Files);
                var reportView = window.FindControl<ImportReportView>("ImportReportView")!;
                Assert.Same(viewModel.Import, reportView.DataContext);
                Assert.Same(viewModel.Import, reportView.FindControl<Grid>("ImportReportContent")!.DataContext);
                Assert.Same(viewModel.Import.Files, reportView.FindControl<ListBox>("ImportReportFiles")!.ItemsSource);
                Assert.Contains($"Принято: 1", viewModel.Import.StatusText);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == viewModel.Import.StatusText);
                await WaitUntilAsync(() => viewModel.SelectedObject?.Id == AssemblyId
                    && viewModel.SelectedCard?.Id == AssemblyId && !viewModel.IsBusy,
                    $"The selected object was lost after import {importNumber}.");
                Assert.Equal(AssemblyId, Assert.IsType<ObjectSearchItemDto>(objectList.SelectedItem).Id);
            }
        }
        finally
        {
            window.Close();
            File.Delete(path);
        }
    }

    /// <summary>
    /// Проверяет доступность повтора при неопределённом результате и восстановление импорта с прежним идентификатором.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [AvaloniaFact]
    public async Task UncertainImportOutcomeKeepsRetryVisibleAndRecoversWithTheSameId()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mini-pdm-{Guid.NewGuid():N}.a3d");
        await File.WriteAllTextAsync(path, "{}");
        var handler = new DesktopApiHandler { ImportFailuresRemaining = 1 };
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        using var viewModel = new MainWindowViewModel(new PdmApiClient(httpClient));
        var picker = new RepeatingFolderPicker(path);
        var window = new MainWindow { DataContext = viewModel, FolderPicker = picker, Width = 1480, Height = 920 };

        try
        {
            window.Show();
            await WaitUntilAsync(() => viewModel.Objects.Count == 3 && !viewModel.IsBusy,
                "Object search did not finish in time.");
            var importButton = window.FindControl<Button>("ImportFolderButton")!;
            importButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !GetIsPicking(window) && !viewModel.Import.IsBusy
                && viewModel.Import.PendingImportId.HasValue,
                "The simulated uncertain import outcome did not remain pending.");

            var pendingId = viewModel.Import.PendingImportId;
            var retryButton = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content?.ToString() == "Проверить / повторить с тем же ID");
            var abandonButton = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content?.ToString() == "Отказаться от повтора этого пакета");
            Assert.True(retryButton.IsVisible);
            Assert.True(retryButton.IsEnabled);
            Assert.True(abandonButton.IsVisible);
            Assert.True(abandonButton.IsEnabled);
            Assert.False(importButton.IsEnabled);
            Assert.False(viewModel.Import.IsBusy);

            await viewModel.Import.RetryCommand.ExecuteAsync();
            await WaitUntilAsync(() => !viewModel.Import.IsBusy && viewModel.Import.PendingImportId is null
                && importButton.IsEnabled && viewModel.Import.Files.Count == 1,
                "Retry did not confirm the import and restore the button.");

            Assert.Equal(new Guid?[] { pendingId, pendingId }, handler.ImportIds.Select(id => (Guid?)id));
            Assert.Single(viewModel.Import.Files);
            Assert.Contains("Принято: 1", viewModel.Import.StatusText);
        }
        finally
        {
            window.Close();
            File.Delete(path);
        }
    }

    /// <summary>
    /// Проверяет отображение редактируемого исторического состава рядом с текущим деревом и расчётом.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [AvaloniaFact]
    public async Task WindowLoadsHistoricalEditableBomBesideCurrentTreeAndCalculation()
    {
        var previousLogSink = Logger.Sink;
        var bindingErrors = new BindingErrorCollector();
        Logger.Sink = bindingErrors;
        var handler = new DesktopApiHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        var viewModel = new MainWindowViewModel(new PdmApiClient(httpClient));
        var window = new MainWindow { DataContext = viewModel, Width = 1480, Height = 920 };

        try
        {
            window.Show();
            await WaitUntilAsync(() => viewModel.Objects.Count == 3 && !viewModel.IsBusy,
                "Object search did not finish in time.");

            viewModel.SelectedObject = viewModel.Objects.Single(x => x.Id == AssemblyId);
            await WaitUntilAsync(() => viewModel.SelectedVersion?.Version == 2 && !viewModel.IsBusy
                && viewModel.Composition.TreeRoots.Count == 1 && viewModel.Composition.TotalMassKg == 12.5m,
                "Current assembly card, tree, and calculation did not load in time.");
            Assert.False(viewModel.CanEditCompositionUi);
            Assert.Equal(CurrentChildId, Assert.Single(viewModel.Composition.TreeRoots).Children.Single().Node.ObjectId);

            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var compositionEditor = FindCompositionEditor(window);
            Assert.False(compositionEditor.IsEffectivelyEnabled);

            viewModel.SelectedHistoryVersion = viewModel.SelectedCard!.Versions.Single(x => x.Version == 1);
            await WaitUntilAsync(() => viewModel.SelectedVersion?.Version == 1 && !viewModel.IsBusy
                && viewModel.Composition.Components.Count == 1 && viewModel.CanEditCompositionUi,
                "Selected historical composition did not load in time.");
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(compositionEditor.IsEffectivelyEnabled);
            var quantityEditor = compositionEditor.GetVisualDescendants().OfType<TextBox>()
                .Single(x => x.Watermark?.ToString() == "Количество");
            Assert.True(quantityEditor.IsEffectivelyEnabled);

            var component = Assert.Single(viewModel.Composition.Components);
            Assert.Equal(HistoricalChildId, component.ChildObjectId);
            Assert.Equal("АБВГ.123456.002 — Historical part", component.Label);
            var treeView = window.GetVisualDescendants().OfType<TreeView>().FirstOrDefault();
            var treeRoot = treeView?.GetVisualDescendants().OfType<TreeViewItem>().FirstOrDefault();
            if (treeRoot is not null)
            {
                treeRoot.IsExpanded = true;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
            Assert.Equal("Действующий состав", window.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == "Действующий состав").Text);
            Assert.Equal("Расчёт действующего состава", window.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == "Расчёт действующего состава").Text);
            var tabHeaders = window.GetVisualDescendants().OfType<TabItem>().Select(x => x.Header?.ToString()).ToArray();
            Assert.Contains("Карточка объекта", tabHeaders);
            Assert.Contains("Состав и расчёт", tabHeaders);
            Assert.Contains("Отчёт импорта", tabHeaders);
            Assert.Contains("Фоновые задачи", tabHeaders);
            Assert.True(window.Bounds.Width >= 1120);
            Assert.True(window.Bounds.Height >= 700);
            Assert.True(bindingErrors.Messages.Count == 0, string.Join(Environment.NewLine, bindingErrors.Messages));

            try
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save("/tmp/pdm-desktop-preview.png");
            }
            catch (NotSupportedException)
            {
                // A rendered frame is optional on headless backends without bitmap capture.
            }
        }
        finally
        {
            window.Close();
            viewModel.Dispose();
            Logger.Sink = previousLogSink;
        }
    }

    /// <summary>
    /// Проверяет, что утверждение версии сохраняет выбранным тот же объект в списке.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [AvaloniaFact]
    public async Task ApprovingVersionKeepsTheSameObjectSelected()
    {
        var handler = new DesktopApiHandler { CurrentAssemblyState = "InWork" };
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        using var viewModel = new MainWindowViewModel(new PdmApiClient(httpClient));
        var window = new MainWindow { DataContext = viewModel, Width = 1480, Height = 920 };

        try
        {
            window.Show();
            await WaitUntilAsync(() => viewModel.Objects.Count == 3 && !viewModel.IsBusy,
                "Object search did not finish in time.");
            var objectList = window.GetVisualDescendants().OfType<ListBox>()
                .Single(list => ReferenceEquals(list.ItemsSource, viewModel.Objects));
            objectList.SelectedItem = viewModel.Objects.Single(item => item.Id == AssemblyId);
            await WaitUntilAsync(() => viewModel.SelectedObject?.Id == AssemblyId
                && viewModel.SelectedVersion?.State == "InWork" && !viewModel.IsBusy,
                "The editable assembly did not finish loading.");

            await viewModel.ApproveCommand.ExecuteAsync();
            await WaitUntilAsync(() => !viewModel.IsBusy && viewModel.SelectedVersion?.State == "Approved",
                "Approval did not finish.");
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(AssemblyId, viewModel.SelectedObject?.Id);
            Assert.Equal(AssemblyId, Assert.IsType<ObjectSearchItemDto>(objectList.SelectedItem).Id);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Ожидает выполнения условия и сообщает об ошибке, если срок ожидания истёк.
    /// </summary>
    /// <param name="condition">Условие, выполнение которого завершает ожидание.</param>
    /// <param name="failureMessage">Сообщение, выводимое при истечении срока ожидания.</param>
    /// <returns>Завершение ожидания после выполнения условия; если срок истекает раньше, утверждение теста завершается ошибкой.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition, string failureMessage)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(condition(), failureMessage);
    }

    /// <summary>
    /// Возвращает текущее состояние выбора папки в главном окне.
    /// </summary>
    /// <param name="window">Окно, состояние которого проверяется.</param>
    /// <returns>Значение, показывающее, выполняется ли сейчас выбор папки.</returns>
    private static bool GetIsPicking(MainWindow window) => (bool)typeof(MainWindow)
        .GetField("_isPicking", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .GetValue(window)!;

    /// <summary>
    /// Находит контейнер редактора состава в визуальном дереве окна.
    /// </summary>
    /// <param name="window">Окно, в котором ищется редактор состава.</param>
    /// <returns>Контейнер редактора состава соответствующего представления.</returns>
    private static Border FindCompositionEditor(MainWindow window)
    {
        var compositionView = window.FindControl<CompositionWorkspaceView>("CompositionWorkspaceView")!;
        return Assert.IsType<Border>(compositionView.FindControl<Border>("CompositionEditor"));
    }

    private sealed class ControlledFolderPicker : IImportFolderPicker
    {
        private readonly TaskCompletionSource<SelectedImportPackage?> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Сигнализирует, что тестовый выбор файлов начался.
        /// </summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Регистрирует начало выбора файлов и возвращает задачу с выбранным пакетом.
        /// </summary>
        /// <param name="owner">Окно-владелец диалога выбора.</param>
        /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
        /// <returns>Задача с выбранным пакетом файлов либо null, если пользователь отменил выбор.</returns>
        public Task<SelectedImportPackage?> PickAsync(Window owner, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            return _result.Task;
        }

        /// <summary>
        /// Завершает ожидающую операцию выбора заданным пакетом.
        /// </summary>
        /// <param name="package">Выбранный пакет файлов либо null при отмене.</param>
        public void Complete(SelectedImportPackage? package) => _result.TrySetResult(package);
    }

    private sealed class RepeatingFolderPicker(string path) : IImportFolderPicker
    {
        /// <summary>
        /// Число вызовов тестового средства выбора файлов.
        /// </summary>
        public int CallCount
        {
            get; private set;
        }

        /// <summary>
        /// Регистрирует начало выбора файлов и возвращает задачу с выбранным пакетом.
        /// </summary>
        /// <param name="owner">Окно-владелец диалога выбора.</param>
        /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
        /// <returns>Задача с выбранным пакетом файлов либо null, если пользователь отменил выбор.</returns>
        public Task<SelectedImportPackage?> PickAsync(Window owner, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<SelectedImportPackage?>(new SelectedImportPackage([path]));
        }
    }

    private sealed class DesktopApiHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        /// <summary>
        /// Идентификаторы запросов импорта, полученных обработчиком.
        /// </summary>
        public List<Guid> ImportIds { get; } = [];
        /// <summary>
        /// Число оставшихся искусственных ошибок импорта.
        /// </summary>
        public int ImportFailuresRemaining
        {
            get; set;
        }
        /// <summary>
        /// Состояние текущей версии тестовой сборки.
        /// </summary>
        public string CurrentAssemblyState { get; set; } = "Approved";

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/api/imports/", StringComparison.Ordinal))
            {
                var importId = Guid.Parse(path["/api/imports/".Length..]);
                if (request.Method == HttpMethod.Get)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        Content = new StringContent("{\"code\":\"NotFound\",\"message\":\"Report not found.\"}", Encoding.UTF8, "application/json")
                    });
                if (request.Method == HttpMethod.Post)
                {
                    ImportIds.Add(importId);
                    if (ImportFailuresRemaining > 0)
                    {
                        ImportFailuresRemaining--;
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                        {
                            Content = new StringContent("{\"code\":\"OutcomeUnknown\",\"message\":\"Temporary response failure.\"}", Encoding.UTF8, "application/json")
                        });
                    }
                    return Ok(new ImportReportDto
                    {
                        ImportId = importId,
                        Files = [new ImportFileResultDto
                        {
                                                        FileName = "test.a3d",
                                                        Status = ImportFileStatus.Accepted,
                                                        Reason = null,
                                                        Action = ImportFileAction.Created,
                                                        Warnings = []
                        }]
                    });
                }
            }

            if (request.Method == HttpMethod.Put && path == $"/api/objects/{AssemblyId:D}/versions/2/state")
            {
                CurrentAssemblyState = "Approved";
                return Ok(new VersionMutationDto
                {
                    ObjectId = AssemblyId,
                    VersionId = CurrentVersionId,
                    VersionNumber = 2,
                    State = CurrentAssemblyState,
                    CurrentVersionId = CurrentVersionId,
                    ConcurrencyToken = ConcurrencyToken,
                    Warnings = []
                });
            }

            if (request.Method == HttpMethod.Get && path == "/api/objects")
                return Ok(new ObjectSearchPageDto
                {
                    Items = [new ObjectSearchItemDto
                    {
                                                Id = AssemblyId,
                                                Type = "Assembly",
                                                Designation = "АБВГ.123456.001",
                                                Name = "Main assembly",
                                                CurrentVersionId = CurrentVersionId,
                                                VersionNumber = 2,
                                                State = CurrentAssemblyState,
                                                UnitMassKg = null,
                                                ConcurrencyToken = ConcurrencyToken,
                                                NoCurrentVersion = false
                    }, new ObjectSearchItemDto
{
        Id = HistoricalChildId,
        Type = "Part",
        Designation = "АБВГ.123456.002",
        Name = "Historical part",
        CurrentVersionId = null,
        VersionNumber = null,
        State = null,
        UnitMassKg = null,
        ConcurrencyToken = Guid.NewGuid(),
        NoCurrentVersion = true
}, new ObjectSearchItemDto
{
        Id = CurrentChildId,
        Type = "StandardPart",
        Designation = null,
        Name = "Current fastener",
        CurrentVersionId = null,
        VersionNumber = null,
        State = null,
        UnitMassKg = null,
        ConcurrencyToken = Guid.NewGuid(),
        NoCurrentVersion = true
}],
                    Offset = 0,
                    Limit = 50,
                    HasMore = false
                });

            if (request.Method == HttpMethod.Get && path == "/api/background-tasks")
                return Ok(Array.Empty<BackgroundTaskDto>());

            if (request.Method == HttpMethod.Get && path == $"/api/objects/{AssemblyId:D}")
            {
                var historical = request.RequestUri.Query.Contains("version=1", StringComparison.Ordinal);
                return Ok(CreateCard(historical));
            }

            if (request.Method == HttpMethod.Get && path == $"/api/objects/{AssemblyId:D}/versions/1/composition")
                return Ok(new VersionCompositionDto
                {
                    ObjectId = AssemblyId,
                    Version = 1,
                    ConcurrencyToken = ConcurrencyToken,
                    Items = [new VersionCompositionItemDto
                    {
                                                ChildObjectId = HistoricalChildId,
                                                Quantity = 4,
                                                Type = "Part",
                                                Designation = "АБВГ.123456.002",
                                                Name = "Historical part",
                                                NoCurrentVersion = false
                    }]
                });

            if (request.Method == HttpMethod.Get && path == $"/api/objects/{AssemblyId:D}/versions/2/composition")
                return Ok(new VersionCompositionDto
                {
                    ObjectId = AssemblyId,
                    Version = 2,
                    ConcurrencyToken = ConcurrencyToken,
                    Items = []
                });

            if (request.Method == HttpMethod.Get && path == $"/api/objects/{AssemblyId:D}/composition")
                return Ok(new CompositionTreeDto
                {
                    RootObjectId = AssemblyId,
                    Nodes = [new CompositionNodeDto
                    {
                                                ObjectId = AssemblyId,
                                                ObjectPath = [AssemblyId],
                                                ParentPath = null,
                                                LocalQuantity = 1,
                                                Type = "Assembly",
                                                Designation = "АБВГ.123456.001",
                                                Name = "Main assembly",
                                                Material = null,
                                                VersionId = CurrentVersionId,
                                                VersionNumber = 2,
                                                State = "Approved",
                                                UnitMassKg = null,
                                                ErrorCode = null,
                                                Error = null
                    }, new CompositionNodeDto
{
        ObjectId = CurrentChildId,
        ObjectPath = [AssemblyId, CurrentChildId],
        ParentPath = [AssemblyId],
        LocalQuantity = 2,
        Type = "StandardPart",
        Designation = null,
        Name = "Current fastener",
        Material = null,
        VersionId = Guid.NewGuid(),
        VersionNumber = 1,
        State = "Approved",
        UnitMassKg = 0.25m,
        ErrorCode = null,
        Error = null
}]
                });

            if (request.Method == HttpMethod.Get && path == $"/api/objects/{AssemblyId:D}/calculations")
                return Ok(new CompositionCalculationDto
                {
                    RootObjectId = AssemblyId,
                    TotalMassKg = 12.5m,
                    IsComplete = true,
                    Items = [new SpecificationItemDto
                    {
                                                ObjectId = CurrentChildId,
                                                Type = "StandardPart",
                                                Designation = null,
                                                Name = "Current fastener",
                                                Material = null,
                                                VersionId = Guid.NewGuid(),
                                                VersionNumber = 1,
                                                Quantity = 2m,
                                                UnitMassKg = 0.25m,
                                                TotalMassKg = 0.5m
                    }],
                    Diagnostics = []
                });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"code\":\"NotFound\",\"message\":\"Test route not found.\"}", Encoding.UTF8, "application/json")
            });
        }

        /// <summary>
        /// Создаёт карточку объекта для ответа тестового API.
        /// </summary>
        /// <param name="historical">Указывает, следует ли вернуть историческую версию.</param>
        /// <returns>Тестовая карточка объекта с текущей или исторической версией.</returns>
        private ObjectCardDto CreateCard(bool historical)
        {
            var selectedId = historical ? HistoricalVersionId : CurrentVersionId;
            var versionNumber = historical ? 1 : 2;
            var selectedState = historical ? "InWork" : CurrentAssemblyState;
            return new ObjectCardDto
            {
                Id = AssemblyId,
                Type = "Assembly",
                Designation = "АБВГ.123456.001",
                Name = "Main assembly",
                CurrentVersionId = CurrentVersionId,
                ConcurrencyToken = ConcurrencyToken,
                SelectedVersion = new ObjectVersionDto
                {
                    Id = selectedId,
                    Version = versionNumber,
                    State = selectedState,
                    Name = historical ? "Historical name" : "Current name",
                    Material = null,
                    UnitMassKg = null,
                    SourceReference = null,
                    IsCurrent = !historical
                },
                Versions = [new ObjectVersionSummaryDto
                {
                                        Id = CurrentVersionId,
                                        Version = 2,
                                        State = CurrentAssemblyState,
                                        IsCurrent = true
                }, new ObjectVersionSummaryDto
{
        Id = HistoricalVersionId,
        Version = 1,
        State = "InWork",
        IsCurrent = false
}

    ],
                ErrorCode = null,
                Error = null
            };
        }

        /// <summary>
        /// Создаёт успешный HTTP-ответ с сериализованным JSON-телом.
        /// </summary>
        /// <param name="response">Объект, который сериализуется в тело ответа.</param>
        /// <returns>HTTP-ответ со статусом 200 и JSON-телом.</returns>
        private static Task<HttpResponseMessage> Ok<T>(T response) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(response, JsonOptions), Encoding.UTF8, "application/json")
        });
    }

    private sealed class BindingErrorCollector : ILogSink
    {
        /// <summary>
        /// Сообщения об ошибках привязки, полученные во время проверки.
        /// </summary>
        public List<string> Messages { get; } = [];

        /// <inheritdoc/>
        public bool IsEnabled(LogEventLevel level, string area) => true;

        /// <inheritdoc/>
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        {
            if (level >= LogEventLevel.Warning && area == LogArea.Binding)
                Messages.Add(messageTemplate);
        }

        /// <inheritdoc/>
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate,
            params object?[] propertyValues)
        {
            if (level >= LogEventLevel.Warning && area == LogArea.Binding)
                Messages.Add($"{messageTemplate} :: {string.Join(", ", propertyValues.Select(x => x?.ToString() ?? "<null>"))}");
        }
    }
}
