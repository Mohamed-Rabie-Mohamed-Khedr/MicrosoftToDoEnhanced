using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MicrosoftToDoEnhanced.Themes;
using MicrosoftToDoEnhanced.ViewModels;

namespace MicrosoftToDoEnhanced;

public partial class MainWindow : Window
{
    private Storyboard? _toastShow;
    private Storyboard? _toastHide;
    private DispatcherTimer? _toastHideTimer;

    public MainWindow(User user)
    {
        InitializeComponent();

        ThemeToggle.IsChecked = ThemeManager.CurrentTheme == AppTheme.Dark;

        var viewModel = new MainViewModel(user, this);
        viewModel.ToastRequested += (_, message) => ShowToast(message);
        viewModel.SignOutRequested += (_, _) =>
        {
            Views.RememberedLoginStore.Clear();
            DataContext = null;
            var login = new Views.LoginView();
            login.Show();
            Close();
        };
        DataContext = viewModel;

        Loaded += (_, _) => SearchBox.Focus();

        _ = viewModel.InitializeAsync();
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        var focused = Keyboard.FocusedElement as DependencyObject;

        if (key == Key.F && modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (key == Key.N && modifiers == ModifierKeys.Control)
        {
            NewTaskBox.Focus();
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control && key is >= Key.D1 and <= Key.D4)
        {
            var index = key - Key.D1;
            if (index < viewModel.SmartViews.Count)
            {
                viewModel.SelectedSmartView = viewModel.SmartViews[index];
                e.Handled = true;
            }
            return;
        }

        var editable = IsEditable(focused);
        var inTaskList = IsWithin(focused, TasksList);
        var inDetail = IsWithin(focused, DetailHost);

        if (key == Key.Delete && !editable)
        {
            if (inDetail && viewModel.DetailTask is { } detailTask)
            {
                detailTask.DeleteTaskCommand.Execute(null);
                e.Handled = true;
            }
            else if (inTaskList && viewModel.SelectedTask is { } selected)
            {
                selected.DeleteTaskCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }

        if (key == Key.Enter && !editable)
        {
            if (inDetail && viewModel.DetailTask is { } openedTask)
            {
                openedTask.SaveTaskCommand.Execute(null);
                e.Handled = true;
            }
            else if (inTaskList && viewModel.SelectedTask is { } taskToToggle)
            {
                viewModel.ToggleTaskStatusCommand.Execute(taskToToggle);
                e.Handled = true;
            }
            return;
        }

        if (key == Key.Escape)
        {
            if (editable)
            {
                TasksList.Focus();
                e.Handled = true;
            }
            else if (viewModel.IsDetailOpen)
            {
                viewModel.CloseDetailsCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }

        if (key == Key.Back && !editable && viewModel.IsDetailOpen)
        {
            viewModel.BackDetailsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (key == Key.Right && !editable && inTaskList
            && viewModel.SelectedTask is { HasChildren: true, IsExpanded: false } expandable)
        {
            expandable.ToggleExpandCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (key == Key.Left && !editable && inTaskList && viewModel.SelectedTask is { } navTask)
        {
            if (navTask.HasChildren && navTask.IsExpanded)
            {
                navTask.ToggleExpandCommand.Execute(null);
                e.Handled = true;
            }
            else if (navTask.TaskParentID is not null)
            {
                viewModel.BackDetailsCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void OnGroupsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        if (DataContext is not MainViewModel viewModel)
            return;

        if (((ListBox)sender).SelectedItem is not Group group)
            return;

        viewModel.OpenGroupChatCommand.Execute(group);
        e.Handled = true;
    }

    private static bool IsEditable(DependencyObject? element) =>
        element is TextBoxBase or PasswordBox or ComboBox or DatePicker or Calendar;

    private static bool IsWithin(DependencyObject? element, FrameworkElement container)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, container))
                return true;

            element = element is Visual
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }

    private void OnThemeToggled(object sender, RoutedEventArgs e)
    {
        var theme = ((ToggleButton)sender).IsChecked == true ? AppTheme.Dark : AppTheme.Light;
        ThemeManager.ApplyTheme(theme);
        ThemeManager.SaveTheme(theme);
    }

    private void OnTaskReorderRequested(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel viewModel
            && e.OldValue is TodoTaskViewModel dragged
            && e.NewValue is TodoTaskViewModel target)
        {
            viewModel.ReorderTasksCommand.Execute(new ReorderPayload(dragged, target));
        }
    }

    private void ShowToast(string message)
    {
        EnsureToastStoryboards();

        _toastShow!.Stop();
        _toastHide!.Stop();
        _toastHideTimer!.Stop();

        ToastText.Text = message;
        Toast.IsHitTestVisible = true;

        _toastShow.Begin();
        _toastHideTimer.Start();
    }

    private void EnsureToastStoryboards()
    {
        if (_toastShow is not null && _toastHide is not null)
            return;

        var show = new Storyboard();
        show.Children.Add(new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        show.Children.Add(new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(220)));
        Storyboard.SetTarget(show.Children[0], Toast);
        Storyboard.SetTargetProperty(show.Children[0], new PropertyPath(OpacityProperty));
        Storyboard.SetTarget(show.Children[1], ToastTransform);
        Storyboard.SetTargetProperty(show.Children[1], new PropertyPath(TranslateTransform.YProperty));

        var hide = new Storyboard();
        hide.Children.Add(new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220)));
        hide.Children.Add(new DoubleAnimation(0, 16, TimeSpan.FromMilliseconds(220)));
        Storyboard.SetTarget(hide.Children[0], Toast);
        Storyboard.SetTargetProperty(hide.Children[0], new PropertyPath(OpacityProperty));
        Storyboard.SetTarget(hide.Children[1], ToastTransform);
        Storyboard.SetTargetProperty(hide.Children[1], new PropertyPath(TranslateTransform.YProperty));
        hide.Completed += (_, _) => Toast.IsHitTestVisible = false;

        _toastShow = show;
        _toastHide = hide;
        _toastHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2400) };
        _toastHideTimer.Tick += (_, _) =>
        {
            _toastHideTimer.Stop();
            _toastHide.Begin();
        };
    }
}
