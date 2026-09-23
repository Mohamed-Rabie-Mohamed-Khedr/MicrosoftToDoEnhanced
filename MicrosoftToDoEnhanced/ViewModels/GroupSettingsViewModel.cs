using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Core.Repositories;

namespace MicrosoftToDoEnhanced.ViewModels;

/// <summary>
/// DataContext of GroupSettingsView. Supports creating/editing a group, member
/// management, the activity feed (AddPostInGroup / GetPreviousCommentsInGroup)
/// and the danger zone for the admin.
/// </summary>
public class GroupSettingsViewModel : ViewModelBase
{
    private const int DefaultLoadWindowEnd = 10;

    private readonly User _currentUser;
    private readonly MainViewModel _owner;
    private readonly Group _workingGroup;
    private readonly Dictionary<int, User> _authorCache = new();

    private int? _groupId;
    private bool _hasMorePosts;
    private int _loadWindowEnd;

    public GroupSettingsViewModel(User currentUser, Group? existing, MainViewModel owner)
    {
        _currentUser = currentUser;
        _owner = owner;

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

        await GroupRepository.AddGroupMemberAsync(groupId, user.UserID);
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

        await GroupRepository.DeleteGroupMemberAsync(groupId, member.UserID);
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
            await GroupRepository.UpdateGroupAsync(existingId, _workingGroup.AdminID, name, description, Color);
            _owner.RaiseToast("Group saved");
        }
        else
        {
            await GroupRepository.AddGroupAsync(_workingGroup.AdminID, name, description, Color);
            _groupId = await GroupRepository.GetGroupIdAsync(_workingGroup.AdminID, name);
            if (_groupId is null)
            {
                _owner.RaiseToast("Could not create the group");
                return;
            }

            await LoadMembersAsync(_groupId.Value);
            _owner.RaiseToast("Group created");
        }

        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private async Task DeleteGroupAsync()
    {
        if (_groupId is not int groupId || !IsOwner)
            return;

        var confirm = MessageBox.Show(
            "This deletes the group, its tasks and its feed for everyone. Continue?",
            "Delete group",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        await GroupRepository.DeleteGroupAsync(groupId);
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

        _loadWindowEnd += DefaultLoadWindowEnd;
        await LoadPostsCoreAsync(groupId);
    }

    private async Task LoadPostsAsync(int groupId)
    {
        _loadWindowEnd = DefaultLoadWindowEnd;
        await LoadPostsCoreAsync(groupId);
    }

    private async Task LoadPostsCoreAsync(int groupId)
    {
        var posts = await PostRepository.GetPreviousCommentsAsync(groupId, _loadWindowEnd);

        Posts.Clear();
        foreach (var post in posts)
        {
            var author = await GetAuthorAsync(post.UserID);
            Posts.Add(new PostItemViewModel(post, author?.ShowName ?? "Unknown", author?.Color ?? "#707070"));
        }

        HasMorePosts = _loadWindowEnd >= DefaultLoadWindowEnd
            && await PostRepository.HasPostsAfterAsync(groupId, _loadWindowEnd);
    }

    private async Task<User?> GetAuthorAsync(int userId)
    {
        if (_authorCache.TryGetValue(userId, out var cached))
            return cached;

        var user = await UserRepository.GetUserAsync(userId);
        if (user is not null)
            _authorCache[userId] = user;
        return user;
    }
}