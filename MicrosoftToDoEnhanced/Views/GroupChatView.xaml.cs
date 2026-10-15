using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MicrosoftToDoEnhanced.ViewModels;

namespace MicrosoftToDoEnhanced.Views;

public partial class GroupChatView : Window
{
    private GroupChatViewModel? _viewModel;

    public GroupChatView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => ScheduleScrollToLatest();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.Posts.CollectionChanged -= OnPostsChanged;

        _viewModel = DataContext as GroupChatViewModel;

        if (_viewModel is not null)
            _viewModel.Posts.CollectionChanged += OnPostsChanged;
    }

    private void OnPostsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null)
            return;

        if (e.NewStartingIndex >= MessagesList.Items.Count - e.NewItems.Count)
            ScheduleScrollToLatest();
    }

    private void ScheduleScrollToLatest() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ScrollToLatest);

    private void ScrollToLatest()
    {
        if (MessagesList.Items.Count > 0)
            MessagesList.ScrollIntoView(MessagesList.Items[MessagesList.Items.Count - 1]);
    }
}
