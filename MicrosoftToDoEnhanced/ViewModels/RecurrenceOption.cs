namespace MicrosoftToDoEnhanced.ViewModels;

/// <summary>
/// Wraps a RepetitionType so the plain ComboBox in TaskDetailPanel renders a readable name.
/// </summary>
public class RecurrenceOption
{
    public RecurrenceOption(RepetitionType type) => Type = type;

    public RepetitionType Type { get; }

    public string Name => Type.RepetitionName;

    public override string ToString() => Name;
}