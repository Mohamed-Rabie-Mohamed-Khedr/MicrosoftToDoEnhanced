using Core.Repositories;

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

public class RecurrenceOption
{
    public RecurrenceOption(RepetitionType type) => Type = type;

    public RepetitionType Type { get; }

    public string Name => Type.RepetitionName;

    public override string ToString() => Name;
}

public class PostItemViewModel
{
    private readonly PostFeedItem _feedItem;

    public PostItemViewModel(PostFeedItem feedItem) => _feedItem = feedItem;

    public string AuthorName => _feedItem.ShowName;
    public string AuthorColor => _feedItem.UserColor;
    public DateTime PostDate => _feedItem.PostDate;
    public string PostContent => _feedItem.PostContent;
}
