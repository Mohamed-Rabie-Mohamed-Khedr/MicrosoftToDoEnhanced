using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MicrosoftToDoEnhanced.Controls;

/// <summary>
/// Reusable task row. Code-behind only carries UI interaction plumbing:
/// click-to-select and drag initiation. Persisting the new order ("Ranking")
/// is delegated to the view-model through ReorderRequested (view concern hook).
/// </summary>
public partial class TaskItem : UserControl
{
    /// <summary>
    /// Raised when the user drops this item onto another task while dragging.
    /// Args carry the dragged item and the drop target; the view-model updates Ranking.
    /// </summary>
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
        DragGrip.PreviewMouseLeftButtonDown += OnDragGripPressed;
        Drop += OnDrop;
    }

    private void OnItemClicked(object sender, MouseButtonEventArgs e)
    {
        // Let the parent ListBox select the container; the VM observes SelectedTask.
        var listBoxItem = FindAncestor<ListBoxItem>(this);
        if (listBoxItem is not null)
            listBoxItem.IsSelected = true;
    }

    private void OnDragGripPressed(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is null) return;
        DragDrop.DoDragDrop(this, new DataObject(typeof(object), DataContext), DragDropEffects.Move);
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(object)) is not { } dragged) return;
        if (ReferenceEquals(dragged, DataContext)) return;

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
