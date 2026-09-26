namespace MicrosoftToDoEnhanced.ViewModels;

public class UserViewModel
{
    public UserViewModel(User user) => User = user;

    public User User { get; }

    public int UserID => User.UserID;
    public string ShowName => User.ShowName;
    public string? UserEmail => User.UserEmail;
    public string Color => User.Color;
    public bool IsAdmin => User.PermissionID == 2;
}
