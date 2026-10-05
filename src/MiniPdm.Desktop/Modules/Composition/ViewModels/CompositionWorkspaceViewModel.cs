using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using MiniPdm.Contracts.Modules.Calculations.DtoModels;
using MiniPdm.Contracts.Modules.Composition.DtoModels;
using MiniPdm.Contracts.Modules.Objects.DtoModels;
using MiniPdm.Desktop.ViewModels;

namespace MiniPdm.Desktop.Modules.Composition.ViewModels;

/// <summary>
/// Хранит состояние дерева, редактируемого состава и расчёта спецификации.
/// </summary>
public sealed class CompositionWorkspaceViewModel : INotifyPropertyChanged
{
    private readonly Action<Guid> _navigate;
    private bool _canEdit;
    private TreeOccurrenceViewModel? _selectedTreeNode;
    private ObjectSearchItemDto? _selectedChild;
    private CompositionItemEditor? _selectedComponent;
    private CompositionCalculationDto? _calculation;

    /// <summary>
    /// Создаёт модель состава с действием перехода к выбранному объекту.
    /// </summary>
    /// <param name="navigate">Действие для открытия объекта по его идентификатору.</param>
    public CompositionWorkspaceViewModel(Action<Guid> navigate)
    {
        _navigate = navigate;
        AddChildCommand = new RelayCommand(AddChild, () => CanEdit && SelectedChild is not null);
        RemoveChildCommand = new RelayCommand(() => { if (SelectedComponent is not null) Components.Remove(SelectedComponent); },
            () => CanEdit && SelectedComponent is not null);
    }

    /// <summary>
    /// Возникает при изменении свойства модели представления.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Возвращает корневые узлы развёрнутого дерева состава.
    /// </summary>
    public ObservableCollection<TreeOccurrenceViewModel> TreeRoots { get; } = [];

    /// <summary>
    /// Возвращает строки состава, доступные для редактирования.
    /// </summary>
    public ObservableCollection<CompositionItemEditor> Components { get; } = [];

    /// <summary>
    /// Возвращает результаты поиска дочерних объектов.
    /// </summary>
    public ObservableCollection<ObjectSearchItemDto> ChildResults { get; } = [];

    /// <summary>
    /// Возвращает диагностические сообщения расчёта.
    /// </summary>
    public ObservableCollection<CalculationDiagnosticDto> Diagnostics { get; } = [];

    /// <summary>
    /// Возвращает рассчитанные строки спецификации.
    /// </summary>
    public ObservableCollection<SpecificationItemDto> Specification { get; } = [];

    /// <summary>
    /// Возвращает команду добавления выбранного объекта в состав.
    /// </summary>
    public RelayCommand AddChildCommand
    {
        get;
    }

    /// <summary>
    /// Возвращает команду удаления выбранной строки состава.
    /// </summary>
    public RelayCommand RemoveChildCommand
    {
        get;
    }

    /// <summary>
    /// Получает или задаёт доступность редактирования состава.
    /// </summary>
    public bool CanEdit
    {
        get => _canEdit; set
        {
            if (Set(ref _canEdit, value))
                RefreshCommands();
        }
    }

    /// <summary>
    /// Получает или задаёт выбранный узел; при выборе запускает переход к его объекту.
    /// </summary>
    public TreeOccurrenceViewModel? SelectedTreeNode
    {
        get => _selectedTreeNode;
        set
        {
            if (Set(ref _selectedTreeNode, value) && value is not null)
                _navigate(value.Node.ObjectId);
        }
    }

    /// <summary>
    /// Получает или задаёт выбранный объект из результатов поиска.
    /// </summary>
    public ObjectSearchItemDto? SelectedChild
    {
        get => _selectedChild;
        set
        {
            if (Set(ref _selectedChild, value))
                AddChildCommand.Refresh();
        }
    }

    /// <summary>
    /// Получает или задаёт выбранную строку редактируемого состава.
    /// </summary>
    public CompositionItemEditor? SelectedComponent
    {
        get => _selectedComponent;
        set
        {
            if (Set(ref _selectedComponent, value))
                RemoveChildCommand.Refresh();
        }
    }

    /// <summary>
    /// Возвращает текущий результат расчёта или <see langword="null"/>, если расчёт не загружен.
    /// </summary>
    public CompositionCalculationDto? Calculation
    {
        get => _calculation;
        private set
        {
            if (Set(ref _calculation, value))
            {
                Notify(nameof(TotalMassKg));
                Notify(nameof(CalculationComplete));
            }
        }
    }

    /// <summary>
    /// Возвращает рассчитанную суммарную массу в килограммах.
    /// </summary>
    public decimal? TotalMassKg => Calculation?.TotalMassKg;

    /// <summary>
    /// Показывает, завершён ли расчёт состава без недостающих данных.
    /// </summary>
    public bool CalculationComplete => Calculation?.IsComplete ?? false;

    /// <summary>
    /// Строит и отображает дерево вхождений из плоского ответа API.
    /// </summary>
    /// <param name="tree">Данные дерева с путями узлов и родительских узлов.</param>
    public void SetTree(CompositionTreeDto tree)
    {
        TreeRoots.Clear();
        var map = new Dictionary<string, TreeOccurrenceViewModel>(StringComparer.Ordinal);
        foreach (var node in tree.Nodes.OrderBy(x => x.ObjectPath.Count))
        {
            var vm = new TreeOccurrenceViewModel(node);
            map[PathKey(node.ObjectPath)] = vm;
            if (node.ParentPath is { Count: > 0 } parentPath && map.TryGetValue(PathKey(parentPath), out var parent))
                parent.Children.Add(vm);
            else
                TreeRoots.Add(vm);
        }
    }

    /// <summary>
    /// Заменяет результат расчёта, список диагностики и строки спецификации.
    /// </summary>
    /// <param name="calculation">Результат расчёта или <see langword="null"/> для очистки данных.</param>
    public void SetCalculation(CompositionCalculationDto? calculation)
    {
        Calculation = calculation;
        Diagnostics.Clear();
        Specification.Clear();
        if (calculation is null)
            return;
        foreach (var item in calculation.Diagnostics)
            Diagnostics.Add(item);
        foreach (var item in calculation.Items)
            Specification.Add(item);
    }

    /// <summary>
    /// Загружает строки состава в редактор и формирует подписи дочерних объектов.
    /// </summary>
    /// <param name="items">Строки состава версии.</param>
    /// <param name="labelFor">Функция получения подписи объекта по его идентификатору.</param>
    public void SetItems(IEnumerable<VersionCompositionItemDto> items, Func<Guid, string> labelFor)
    {
        Components.Clear();
        foreach (var item in items)
            Components.Add(new CompositionItemEditor(item.ChildObjectId, item.Quantity, labelFor(item.ChildObjectId)));
    }

    /// <summary>
    /// Очищает дерево, результат расчёта, диагностику и спецификацию.
    /// </summary>
    public void ClearCurrent()
    {
        TreeRoots.Clear();
        SetCalculation(null);
    }

    private void AddChild()
    {
        if (SelectedChild is null)
            return;
        var existing = Components.FirstOrDefault(x => x.ChildObjectId == SelectedChild.Id);
        if (existing is not null)
        {
            if (!existing.TryGetPositiveQuantity(out var quantity) || quantity == int.MaxValue)
                return;
            existing.QuantityText = (quantity + 1).ToString(CultureInfo.InvariantCulture);
            return;
        }
        Components.Add(new CompositionItemEditor(SelectedChild.Id, 1, ObjectLabel(SelectedChild)));
    }

    private void RefreshCommands()
    {
        AddChildCommand.Refresh();
        RemoveChildCommand.Refresh();
    }
    private static string PathKey(IReadOnlyList<Guid> path) => string.Join("/", path);
    private static string ObjectLabel(ObjectSearchItemDto item) => item.Type == "StandardPart"
        ? item.Name ?? "Стандартное изделие" : $"{item.Designation ?? "Без обозначения"} — {item.Name ?? item.Type}";
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

/// <summary>
/// Представляет узел дерева состава вместе с его дочерними вхождениями.
/// </summary>
public sealed class TreeOccurrenceViewModel
{
    /// <summary>
    /// Создаёт узел представления для элемента дерева API.
    /// </summary>
    /// <param name="node">Данные вхождения состава.</param>
    public TreeOccurrenceViewModel(CompositionNodeDto node) => Node = node;

    /// <summary>
    /// Возвращает данные объекта и вхождения, представленного узлом.
    /// </summary>
    public CompositionNodeDto Node
    {
        get;
    }

    /// <summary>
    /// Возвращает дочерние узлы текущего вхождения.
    /// </summary>
    public ObservableCollection<TreeOccurrenceViewModel> Children { get; } = [];

    /// <summary>
    /// Возвращает краткую подпись с обозначением, локальным количеством и ошибкой узла.
    /// </summary>
    public string Label => $"{Node.Designation ?? Node.Name ?? Node.ObjectId.ToString()}  × {Node.LocalQuantity}" +
        (Node.Error is null ? string.Empty : $"  ⚠ {Node.Error}");
}

/// <summary>
/// Хранит редактируемое количество дочернего объекта в строке состава.
/// </summary>
/// <param name="childObjectId">Идентификатор дочернего объекта.</param>
/// <param name="quantity">Исходное количество компонента.</param>
/// <param name="label">Подпись компонента для интерфейса.</param>
public sealed class CompositionItemEditor(Guid childObjectId, int quantity, string label) : INotifyPropertyChanged
{
    private string _quantityText = quantity.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Возвращает идентификатор дочернего объекта.
    /// </summary>
    public Guid ChildObjectId { get; } = childObjectId;

    /// <summary>
    /// Возвращает подпись компонента.
    /// </summary>
    public string Label { get; } = label;

    /// <summary>
    /// Получает или задаёт текст количества, вводимый пользователем.
    /// </summary>
    public string QuantityText
    {
        get => _quantityText; set
        {
            if (_quantityText == value)
                return;
            _quantityText = value;
            PropertyChanged?.Invoke(this, new(nameof(QuantityText)));
        }
    }

    /// <summary>
    /// Проверяет, что введено положительное целое количество.
    /// </summary>
    /// <param name="quantity">При успехе содержит разобранное количество; иначе значение по умолчанию.</param>
    /// <returns><see langword="true"/>, если текст содержит положительное целое число.</returns>
    public bool TryGetPositiveQuantity(out int quantity) => int.TryParse(QuantityText, NumberStyles.None,
        CultureInfo.InvariantCulture, out quantity) && quantity > 0;

    /// <summary>
    /// Возникает при изменении количества компонента.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;
}
