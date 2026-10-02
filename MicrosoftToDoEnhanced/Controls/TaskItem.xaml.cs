using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MicrosoftToDoEnhanced.Controls;

public partial class TaskItem : UserControl
{
    private const string DragFormat = "MicrosoftToDoEnhanced.TaskItem.DragSource";

    public static readonly RoutedEvent ReorderRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(ReorderRequested), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<object>), typeof(TaskItem));

    public event RoutedPropertyChangedEventHandler<object> ReorderRequested
    {
        add => AddHandler(ReorderRequestedEvent, value);
        remove => RemoveHandler(ReorderRequestedEvent, value);
    }

    public TaskItem()
    {
        InitializeComponent();
        AllowDrop = true;
        DragGrip.PreviewMouseLeftButtonDown += OnDragGripPressed;
        Root.DragOver += OnDragOver;
        Root.Drop += OnDrop;
    }

    private void OnItemClicked(object sender, MouseButtonEventArgs e)
    {
        var listBoxItem = FindAncestor<ListBoxItem>(this);
        if (listBoxItem is not null)
            listBoxItem.IsSelected = true;
    }

    private static bool ReorderAllowed(object? dataContext) =>
        dataContext is not ViewModels.TodoTaskViewModel task || task.CanReorder;

    private void OnDragGripPressed(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ViewModels.TodoTaskViewModel task || !task.CanReorder)
        {
            e.Handled = true;
            return;
        }

        var payload = new DataObject(DragFormat, task);
        DragDrop.DoDragDrop(this, payload, DragDropEffects.Move);
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = AcceptsDrop(e) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private bool AcceptsDrop(DragEventArgs e) =>
        TryGetDraggedTask(e, out var dragged)
        && ReorderAllowed(DataContext)
        && !ReferenceEquals(dragged, DataContext);

    private static bool TryGetDraggedTask(DragEventArgs e, out ViewModels.TodoTaskViewModel dragged)
    {
        dragged = null!;
        if (!e.Data.GetDataPresent(DragFormat))
            return false;

        if (e.Data.GetData(DragFormat) is not ViewModels.TodoTaskViewModel task)
            return false;

        dragged = task;
        return true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!TryGetDraggedTask(e, out var dragged))
            return;

        if (!ReorderAllowed(DataContext) || ReferenceEquals(dragged, DataContext))
            return;

        RaiseEvent(new RoutedPropertyChangedEventArgs<object>(dragged, DataContext, ReorderRequestedEvent));
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
