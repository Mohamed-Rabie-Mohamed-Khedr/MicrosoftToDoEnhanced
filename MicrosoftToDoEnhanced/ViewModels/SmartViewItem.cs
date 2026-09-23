namespace MicrosoftToDoEnhanced.ViewModels;

public enum SmartViewKind
{
    All,
    Today,
    Important,
    Planned
}

public class SmartViewItem : ViewModelBase
{
    private int _count;

    public SmartViewItem(string name, string color, SmartViewKind kind)
    {
        Name = name;
        Color = color;
        Kind = kind;
    }

    public string Name { get; }
    public string Color { get; }
    public SmartViewKind Kind { get; }

    public int Count
    {
        get => _count;
        set => SetProperty(ref _count, value);
    }
}