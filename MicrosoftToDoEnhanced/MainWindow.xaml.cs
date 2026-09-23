using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MicrosoftToDoEnhanced.Themes;
using MicrosoftToDoEnhanced.ViewModels;

namespace MicrosoftToDoEnhanced
{
    /// <summary>
    /// Three-pane shell. Code-behind carries UI-only plumbing:
    /// theme toggle, toast animation, and the drag-reorder notification hook.
    /// All business behavior is exposed via view-model bindings in XAML.
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        public MainWindow(User user)
        {
            InitializeComponent();

            var viewModel = new MainViewModel(user, this);
            viewModel.ToastRequested += (_, message) => ShowToast(message);
            viewModel.SignOutRequested += (_, _) =>
            {
                DataContext = null;
                var login = new Views.LoginView();
                login.Show();
                Close();
            };
            DataContext = viewModel;

            _ = InitializeViewModelAsync(viewModel);
        }

        private static async System.Threading.Tasks.Task InitializeViewModelAsync(MainViewModel viewModel)
        {
            try
            {
                await viewModel.InitializeAsync();
            }
            catch (Exception)
            {
                // Errors are surfaced to the user by the view-model's own handling.
            }
        }

        private void OnThemeToggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle)
                ThemeManager.ApplyTheme(toggle.IsChecked == true ? AppTheme.Dark : AppTheme.Light);
        }

        // Fired by TaskItem after a drag-drop. Ranking persistence lives in the VM.
        private void OnTaskReorderRequested(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is not MainViewModel viewModel)
                return;
            if (e.OldValue is not TodoTaskViewModel dragged || e.NewValue is not TodoTaskViewModel target)
                return;

            viewModel.ReorderTasksCommand.Execute(new ReorderPayload(dragged, target));
        }

        /// <summary>Lightweight toast helper (UI concern).</summary>
        public void ShowToast(string message)
        {
            ToastText.Text = message;
            Toast.IsHitTestVisible = true;

            var show = new Storyboard();
            show.Children.Add(new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            show.Children.Add(new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(220)));
            Storyboard.SetTarget(show.Children[0], Toast);
            Storyboard.SetTargetProperty(show.Children[0], new PropertyPath(OpacityProperty));
            Storyboard.SetTarget(show.Children[1], ToastTransform);
            Storyboard.SetTargetProperty(show.Children[1], new PropertyPath(TranslateTransform.YProperty));

            var hide = new Storyboard { BeginTime = TimeSpan.FromMilliseconds(2400) };
            hide.Children.Add(new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220)));
            hide.Children.Add(new DoubleAnimation(0, 16, TimeSpan.FromMilliseconds(220)));
            Storyboard.SetTarget(hide.Children[0], Toast);
            Storyboard.SetTargetProperty(hide.Children[0], new PropertyPath(OpacityProperty));
            Storyboard.SetTarget(hide.Children[1], ToastTransform);
            Storyboard.SetTargetProperty(hide.Children[1], new PropertyPath(TranslateTransform.YProperty));
            hide.Completed += (_, _) => Toast.IsHitTestVisible = false;

            show.Begin();
            hide.Begin();
        }
    }
}