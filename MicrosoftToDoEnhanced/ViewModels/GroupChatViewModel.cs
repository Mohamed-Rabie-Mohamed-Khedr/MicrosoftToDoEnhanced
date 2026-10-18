using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Core.Repositories;

namespace MicrosoftToDoEnhanced.ViewModels;

public class GroupMemberItem(User user, bool isAdmin, bool isSelf, bool isViewerOwner)
{
    private User User { get; } = user;

    public int UserID => User.UserID;
    public string ShowName => User.ShowName;
    public string? UserEmail => User.UserEmail;
    public string Color => User.Color;
    public bool IsAdmin { get; } = isAdmin;
    public bool CanRemove { get; } = (isViewerOwner || isSelf) && !isAdmin;
}

public class GroupTaskItem(TodoTask task)
{
    public string TaskName { get; } = task.TaskName;
    public bool IsCompleted { get; } = task.TaskStatusID == (int)TaskState.Completed;
    public string? Description { get; } = task.Description;
    public string Color { get; } = task.Color;
}

public class GroupChatViewModel : ViewModelBase
{
    private const int PageSize = 20;

    private readonly User _currentUser;
    private readonly MainViewModel _owner;
    private readonly Group _group;
    private int? _lowestPostId;
    private int _highestPostId;
    private bool _hasMorePosts;
    private bool _pollInProgress;
    private DispatcherTimer? _pollTimer;
    private string _groupName;
    private string? _groupDescription;
    private string _color;
    private string? _newMemberUserName;
    private string? _newPostContent;

    public GroupChatViewModel(User currentUser, Group group, MainViewModel owner)
    {
        _currentUser = currentUser;
        _group = group;
        _owner = owner;
        _groupName = group.GroupName;
        _groupDescription = group.GroupDescription;
        _color = group.Color;
        IsOwner = group.AdminID == currentUser.UserID;

        AddMemberCommand = new AsyncRelayCommand(AddMemberAsync, onError: OnCommandError);
        RemoveMemberCommand = new AsyncRelayCommand<GroupMemberItem>(RemoveMemberAsync, onError: OnCommandError);
        AddPostCommand = new AsyncRelayCommand(AddPostAsync, onError: OnCommandError);
        LoadMorePostsCommand = new AsyncRelayCommand(() => LoadPostsAsync(loadMore: true), onError: OnCommandError);
        OpenEditGroupCommand = new RelayCommand(OpenEditGroup);
        EditTasksCommand = new RelayCommand(() =>
        {
            EditTasksRequested = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnCommandError(Exception exception) =>
        _owner.RaiseToast(DescribeError(exception));

    public event EventHandler? RequestClose;
    public event EventHandler? RequestEditGroup;

    public ICommand AddMemberCommand { get; }
    public ICommand RemoveMemberCommand { get; }
    public ICommand AddPostCommand { get; }
    public ICommand LoadMorePostsCommand { get; }
    public ICommand OpenEditGroupCommand { get; }
    public ICommand EditTasksCommand { get; }

    public ObservableCollection<GroupMemberItem> Members { get; } = new();
    public ObservableCollection<PostItemViewModel> Posts { get; } = new();
    public ObservableCollection<GroupTaskItem> Tasks { get; } = new();

    public string GroupName
    {
        get => _groupName;
        private set => SetProperty(ref _groupName, value);
    }

    public string? GroupDescription
    {
        get => _groupDescription;
        private set => SetProperty(ref _groupDescription, value);
    }

    public string Color
    {
        get => _color;
        private set => SetProperty(ref _color, value);
    }

    public string? NewMemberUserName
    {
        get => _newMemberUserName;
        set => SetProperty(ref _newMemberUserName, value);
    }

    public string? NewPostContent
    {
        get => _newPostContent;
        set => SetProperty(ref _newPostContent, value);
    }

    public bool IsOwner { get; }

    public bool HasMorePosts
    {
        get => _hasMorePosts;
        private set => SetProperty(ref _hasMorePosts, value);
    }

    public bool EditTasksRequested { get; private set; }

    public async Task InitializeAsync()
    {
        await LoadMembersAsync();
        await LoadPostsAsync(loadMore: false);
        await LoadTasksAsync();
        StartPolling();
    }

    public void StopPolling() => _pollTimer?.Stop();

    private void StartPolling()
    {
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pollTimer.Tick += async (_, _) => await PollAsync();
        _pollTimer.Start();
    }

    private async Task PollAsync()
    {
        if (_pollInProgress)
            return;

        _pollInProgress = true;
        try
        {
            var posts = await PostRepository.GetPostsAsync(_group.GroupID, _currentUser.UserID, null, PageSize);
            var fresh = posts.Where(p => p.PostID > _highestPostId).ToList();
            if (fresh.Count == 0)
                return;

            _highestPostId = fresh.Max(p => p.PostID);
            foreach (var post in fresh.AsEnumerable().Reverse())
                Posts.Add(new PostItemViewModel(post));
        }
        catch (UnauthorizedAccessException)
        {
            _pollTimer?.Stop();
            _owner.RaiseToast("You are no longer a member of this group");
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _pollInProgress = false;
        }
    }

    public void ApplyGroupUpdate(Group updated)
    {
        _group.GroupName = updated.GroupName;
        _group.GroupDescription = updated.GroupDescription;
        _group.Color = updated.Color;
        GroupName = updated.GroupName;
        GroupDescription = updated.GroupDescription;
        Color = updated.Color;
    }

    public Group GetGroupSnapshot() => new()
    {
        GroupID = _group.GroupID,
        AdminID = _group.AdminID,
        GroupName = _group.GroupName,
        GroupDescription = _group.GroupDescription,
        Color = _group.Color
    };

    private void OpenEditGroup() => RequestEditGroup?.Invoke(this, EventArgs.Empty);

    private async Task LoadTasksAsync()
    {
        var tasks = await TaskRepository.GetGroupParentTasksAsync(_group.GroupID, _currentUser.UserID);
        Tasks.Clear();
        foreach (var task in tasks)
            Tasks.Add(new GroupTaskItem(task));
    }

    private async Task LoadMembersAsync()
    {
        var users = await GroupRepository.GetGroupMembersAsync(_group.GroupID, _currentUser.UserID);
        Members.Clear();
        foreach (var user in users)
        {
            var isAdmin = user.UserID == _group.AdminID;
            var isSelf = user.UserID == _currentUser.UserID;
            Members.Add(new GroupMemberItem(user, isAdmin, isSelf, IsOwner));
        }
    }

    private async Task AddMemberAsync()
    {
        if (!IsOwner || string.IsNullOrWhiteSpace(NewMemberUserName))
            return;

        var user = await UserRepository.GetUserByUserNameAsync(NewMemberUserName.Trim());
        if (user is null)
        {
            _owner.RaiseToast("No user has that user name");
            return;
        }

        if (Members.Any(m => m.UserID == user.UserID))
        {
            _owner.RaiseToast("That user is already a member");
            return;
        }

        await GroupRepository.AddGroupMemberAsync(_group.GroupID, user.UserID, _currentUser.UserID);
        NewMemberUserName = string.Empty;
        await LoadMembersAsync();
        _owner.RaiseToast("Member added");
    }

    private async Task RemoveMemberAsync(GroupMemberItem? member)
    {
        if (member is null || !member.CanRemove)
            return;

        await GroupRepository.DeleteGroupMemberAsync(_group.GroupID, member.UserID, _currentUser.UserID);

        if (member.UserID == _currentUser.UserID)
        {
            _owner.RaiseToast("You left the group");
            RequestClose?.Invoke(this, EventArgs.Empty);
            return;
        }

        Members.Remove(member);
        _owner.RaiseToast("Member removed");
    }

    private async Task AddPostAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPostContent))
            return;

        await PostRepository.AddPostAsync(_group.GroupID, _currentUser.UserID, NewPostContent.Trim());
        NewPostContent = string.Empty;

        var latest = await PostRepository.GetPostsAsync(_group.GroupID, _currentUser.UserID, null, 1);
        if (latest.Count > 0 && (Posts.Count == 0 || Posts[^1].PostID != latest[0].PostID))
            Posts.Add(new PostItemViewModel(latest[0]));

        if (latest.Count > 0)
            _highestPostId = Math.Max(_highestPostId, latest[0].PostID);
    }

    private async Task LoadPostsAsync(bool loadMore)
    {
        var posts = await PostRepository.GetPostsAsync(
            _group.GroupID, _currentUser.UserID, loadMore ? _lowestPostId : null, PageSize);

        if (!loadMore)
        {
            _lowestPostId = null;
            Posts.Clear();
        }

        HasMorePosts = posts.Count == PageSize;
        if (posts.Count > 0)
        {
            _lowestPostId = posts.Min(p => p.PostID);
            _highestPostId = Math.Max(_highestPostId, posts.Max(p => p.PostID));
        }

        var index = 0;
        foreach (var post in posts.AsEnumerable().Reverse())
            Posts.Insert(index++, new PostItemViewModel(post));
    }
}
