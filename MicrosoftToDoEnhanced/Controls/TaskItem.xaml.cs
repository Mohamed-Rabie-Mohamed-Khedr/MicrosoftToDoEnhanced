using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MicrosoftToDoEnhanced.Controls;

public partial class TaskItem : UserControl
{
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
        var listBoxItem = FindAncestor<ListBoxItem>(this);
        if (listBoxItem is not null)
            listBoxItem.IsSelected = true;
    }

    /// <summary>
    /// Today and Planned are ordered by date and Important is a filtered subset of
    /// "All", so a drop there would move items in a way the user did not intend.
    /// </summary>
    private static bool ReorderAllowed(object? dataContext) =>
        dataContext is not ViewModels.TodoTaskViewModel task || task.CanReorder;

    private void OnDragGripPressed(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is null || !ReorderAllowed(DataContext))
        {
            e.Handled = true;
            return;
        }

        DragDrop.DoDragDrop(this, new DataObject(typeof(object), DataContext), DragDropEffects.Move);
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!ReorderAllowed(DataContext))
            return;

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
