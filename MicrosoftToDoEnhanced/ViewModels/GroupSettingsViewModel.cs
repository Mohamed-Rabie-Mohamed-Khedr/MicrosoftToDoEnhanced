using System.Collections.ObjectModel;
using System.Windows.Input;
using Core.Repositories;
using MicrosoftToDoEnhanced.Services;

namespace MicrosoftToDoEnhanced.ViewModels;

/// <summary>
/// DataContext of GroupSettingsView. Supports creating/editing a group, member
/// management, the activity feed (keyset-paged group posts) and the danger zone
/// for the admin.
/// </summary>
public class GroupSettingsViewModel : ViewModelBase
{
    private const int PageSize = 20;

    private readonly User _currentUser;
    private readonly MainViewModel _owner;
    private readonly Group _workingGroup;
    private readonly IConfirmationService _confirmation;

    private int? _groupId;
    private bool _hasMorePosts;
    private int? _lowestPostId;

    public GroupSettingsViewModel(
        User currentUser, Group? existing, MainViewModel owner, IConfirmationService confirmation)
    {
        _currentUser = currentUser;
        _owner = owner;
        _confirmation = confirmation;

        _workingGroup = existing is null
            ? new Group { AdminID = currentUser.UserID, GroupName = string.Empty, Color = "#0078D4" }
            : new Group
            {
                GroupID = existing.GroupID,
                AdminID = existing.AdminID,
                GroupName = existing.GroupName,
                GroupDescription = existing.GroupDescription,
                Color = existing.Color
            };

        _groupId = existing?.GroupID;
        GroupName = _workingGroup.GroupName;
        GroupDescription = _workingGroup.GroupDescription;
        Color = _workingGroup.Color;

        AddMemberCommand = new AsyncRelayCommand(async () => await AddMemberAsync());
        RemoveMemberCommand = new AsyncRelayCommand<UserViewModel>(async member => await RemoveMemberAsync(member));
        DeleteGroupCommand = new AsyncRelayCommand(async () => await DeleteGroupAsync());
        SaveGroupCommand = new AsyncRelayCommand(async () => await SaveGroupAsync());
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));
        AddPostCommand = new AsyncRelayCommand(async () => await AddPostAsync());
        LoadMorePostsCommand = new AsyncRelayCommand(async () => await LoadMorePostsAsync());
    }

    public event EventHandler? RequestClose;

    public ICommand AddMemberCommand { get; }
    public ICommand RemoveMemberCommand { get; }
    public ICommand DeleteGroupCommand { get; }
    public ICommand SaveGroupCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand AddPostCommand { get; }
    public ICommand LoadMorePostsCommand { get; }

    public string? GroupName { get; set; }
    public string? GroupDescription { get; set; }
    public string Color { get; set; } = "#0078D4";

    public List<string> ColorChoices { get; } = new()
    {
        "#0078D4", "#5C2D91", "#D83B01", "#107C10", "#C239B3",
        "#008575", "#8764B8", "#498205", "#CA5010", "#E74856",
        "#00B7C3", "#FF8C00", "#767676", "#000000"
    };

    public ObservableCollection<UserViewModel> Members { get; } = new();
    public ObservableCollection<PostItemViewModel> Posts { get; } = new();

    public string? NewMemberEmail { get; set; }
    public string? NewPostContent { get; set; }

    public bool IsOwner => _workingGroup.AdminID == _currentUser.UserID;

    /// <summary>The working group id once it exists (null while creating an unsaved group).</summary>
    public int? GroupId => _groupId;

    public bool HasMorePosts
    {
        get => _hasMorePosts;
        private set => SetProperty(ref _hasMorePosts, value);
    }

    public async Task InitializeAsync()
    {
        if (_groupId is not int groupId)
            return;

        await LoadMembersAsync(groupId);
        await LoadPostsAsync(groupId);
    }

    private async Task LoadMembersAsync(int groupId)
    {
        var members = await GroupRepository.GetGroupMembersAsync(groupId);
        Members.Clear();
        foreach (var user in members)
            Members.Add(new UserViewModel(user));
    }

    private async Task AddMemberAsync()
    {
        if (_groupId is not int groupId)
        {
            _owner.RaiseToast("Save the group first, then invite members");
            return;
        }

        if (string.IsNullOrWhiteSpace(NewMemberEmail))
            return;

        var user = await UserRepository.GetUserByEmailAsync(NewMemberEmail.Trim());
        if (user is null)
        {
            _owner.RaiseToast("No user has that email address");
            return;
        }

        if (Members.Any(m => m.UserID == user.UserID))
        {
            _owner.RaiseToast("That user is already a member");
            return;
        }

        await GroupRepository.AddGroupMemberAsync(groupId, user.UserID, _currentUser.UserID);
        NewMemberEmail = string.Empty;
        OnPropertyChanged(nameof(NewMemberEmail));
        await LoadMembersAsync(groupId);
        _owner.RaiseToast("Member added");
    }

    private async Task RemoveMemberAsync(UserViewModel? member)
    {
        if (member is null || !IsOwner || _groupId is not int groupId)
            return;

        if (member.UserID == _workingGroup.AdminID)
            return;

        await GroupRepository.DeleteGroupMemberAsync(groupId, member.UserID, _currentUser.UserID);
        Members.Remove(member);
        _owner.RaiseToast("Member removed");
    }

    private async Task SaveGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(GroupName))
        {
            _owner.RaiseToast("Group name is required");
            return;
        }

        var name = GroupName.Trim();
        var description = string.IsNullOrWhiteSpace(GroupDescription) ? null : GroupDescription.Trim();

        if (_groupId is int existingId)
        {
            await GroupRepository.UpdateGroupAsync(existingId, _currentUser.UserID, name, description, Color);
            _owner.RaiseToast("Group saved");
            RequestClose?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Create path: keep the dialog open so the user can invite members/post right away.
        // The dialog only closes when the user explicitly does so.
        _groupId = await GroupRepository.AddGroupAsync(_workingGroup.AdminID, name, description, Color);
        _workingGroup.GroupID = _groupId.Value;
        await LoadMembersAsync(_groupId.Value);
        _owner.RaiseToast("Group created");
    }

    private async Task DeleteGroupAsync()
    {
        if (_groupId is not int groupId || !IsOwner)
            return;

        var confirmed = _confirmation.Confirm(
            "Delete group",
            "This deletes the group, its tasks and its feed for everyone. Continue?");
        if (!confirmed)
            return;

        await GroupRepository.DeleteGroupAsync(groupId, _currentUser.UserID);
        _owner.RaiseToast("Group deleted");
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private async Task AddPostAsync()
    {
        if (_groupId is not int groupId)
        {
            _owner.RaiseToast("Save the group first");
            return;
        }

        if (string.IsNullOrWhiteSpace(NewPostContent))
            return;

        await PostRepository.AddPostAsync(groupId, _currentUser.UserID, NewPostContent.Trim());
        NewPostContent = string.Empty;
        OnPropertyChanged(nameof(NewPostContent));
        await LoadPostsAsync(groupId);
        _owner.RaiseToast("Posted");
    }

    private async Task LoadMorePostsAsync()
    {
        if (_groupId is not int groupId)
            return;

        await AppendPostsAsync(groupId);
    }

    private async Task LoadPostsAsync(int groupId)
    {
        _lowestPostId = null;
        Posts.Clear();
        await AppendPostsAsync(groupId);
    }

    /// <summary>
    /// Fetches the next page using the keyset cursor and appends it to the existing
    /// collection. Never clears: only a fresh load (<see cref="LoadPostsAsync"/>) does that.
    /// </summary>
    private async Task AppendPostsAsync(int groupId)
    {
        var posts = await PostRepository.GetPostsAsync(groupId, _currentUser.UserID, _lowestPostId, PageSize);

        foreach (var post in posts)
            Posts.Add(new PostItemViewModel(post));

        HasMorePosts = posts.Count == PageSize;
        if (posts.Count > 0)
            _lowestPostId = posts.Min(p => p.PostID);
    }
}