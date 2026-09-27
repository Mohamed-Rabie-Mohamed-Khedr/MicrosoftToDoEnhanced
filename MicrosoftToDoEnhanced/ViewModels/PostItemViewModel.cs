using Core.Repositories;

namespace MicrosoftToDoEnhanced.ViewModels;

public class PostItemViewModel
{
    public PostItemViewModel(PostFeedItem feedItem)
    {
        FeedItem = feedItem;
    }

    public PostFeedItem FeedItem { get; }

    public int PostID => FeedItem.PostID;
    public int UserID => FeedItem.UserID;
    public string AuthorName => FeedItem.ShowName;
    public string AuthorColor => FeedItem.UserColor;
    public DateTime PostDate => FeedItem.PostDate;
    public string PostContent => FeedItem.PostContent;
    public int LikeCount => FeedItem.LikeCount;
    public bool LikedByMe => FeedItem.LikedByMe;
    public bool IsMine => FeedItem.IsMine;
}