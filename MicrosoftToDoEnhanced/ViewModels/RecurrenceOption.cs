namespace MicrosoftToDoEnhanced.ViewModels;

public class RecurrenceOption
{
    public RecurrenceOption(RepetitionType type) => Type = type;

    public RepetitionType Type { get; }

    public string Name => Type.RepetitionName;

    public override string ToString() => Name;
}
