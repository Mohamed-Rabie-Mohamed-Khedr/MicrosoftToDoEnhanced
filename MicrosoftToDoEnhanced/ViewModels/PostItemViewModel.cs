namespace MicrosoftToDoEnhanced.ViewModels;

public class PostItemViewModel
{
    public PostItemViewModel(PostInGroup post, string authorName, string authorColor)
    {
        Post = post;
        AuthorName = authorName;
        AuthorColor = authorColor;
    }

    public PostInGroup Post { get; }

    public string AuthorName { get; }
    public string AuthorColor { get; }
    public DateTime PostDate => Post.PostDate;
    public string PostContent => Post.PostContent;
}