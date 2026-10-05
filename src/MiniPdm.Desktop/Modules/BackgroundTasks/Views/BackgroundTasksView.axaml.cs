using Avalonia.Controls;
using Avalonia;
using MiniPdm.Desktop.Modules.BackgroundTasks.ViewModels;

namespace MiniPdm.Desktop.Modules.BackgroundTasks.Views;

/// <summary>
/// Представляет панель управления фоновыми задачами.
/// </summary>
public partial class BackgroundTasksView : UserControl
{
    /// <summary>
    /// Создаёт представление фоновых задач и загружает его разметку.
    /// </summary>
    public BackgroundTasksView() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is BackgroundTasksViewModel viewModel)
            viewModel.CancelActivePolling();
        base.OnDetachedFromVisualTree(e);
    }
}
