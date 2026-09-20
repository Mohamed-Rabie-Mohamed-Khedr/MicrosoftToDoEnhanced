using System.Windows;

namespace MicrosoftToDoEnhanced.Views;

public partial class GroupSettingsView : Window
{
    public GroupSettingsView()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
