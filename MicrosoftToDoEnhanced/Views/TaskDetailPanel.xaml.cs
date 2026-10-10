using System.Windows;
using System.Windows.Controls;

namespace MicrosoftToDoEnhanced.Views;

public partial class TaskDetailPanel : UserControl
{
    public TaskDetailPanel()
    {
        InitializeComponent();
    }

    private void AddStepButton_Click(object sender, RoutedEventArgs e) => StepInput.Focus();
}
