using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using Core.Repositories;
using MicrosoftToDoEnhanced.Views;

namespace MicrosoftToDoEnhanced.ViewModels;

public sealed record ReorderPayload(TodoTaskViewModel Dragged, TodoTaskViewModel Target);

/// <summary>
/// DataContext of MainWindow. Satisfies the placeholder bindings listed in
/// MainWindow.xaml (CurrentUser, SmartViews, Groups, Tasks, SelectedTask,
/// NewTaskTitle, AddTaskCommand, SortByImportance, ShowCompleted, SearchText,
/// OpenGroupSettingsCommand, CreateGroupCommand, SignOutCommand, ...).
/// Never touches SqlConnection/SqlCommand directly - it only talks to Core repositories.
/// </summary>
public class MainViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly Dictionary<int, string> _statusNames = new();
    private readonly Dictionary<int, string> _levelNames = new();
    private readonly List<User> _assignees = new();
    private readonly List<RecurrenceOption> _recurrenceOptions = new();

    private Group? _selectedGroup;
    private SmartViewItem? _selectedSmartView;
    private TodoTaskViewModel? _selectedTask;
    private bool _sortByImportance;
    private bool _showCompleted;
    private string? _searchText;
    private string? _newTaskTitle;
    private string _currentViewTitle = "All";
    private string? _currentViewSubtitle;
    private int? _preservedSelectedTaskId;
    private List<TodoTask> _sourceTasks = new();
    private Dictionary<int, PlannedTask> _sourcePlanned = new();
    private bool _initialized;

    public MainViewModel(User user, Window window)
    {
        User = user;
        CurrentUser = new UserViewModel(user);
        _window = window;

        SmartViews.Add(new SmartViewItem("All", "#707070", SmartViewKind.All));
        SmartViews.Add(new SmartViewItem("Today", "#0078D4", SmartViewKind.Today));
        SmartViews.Add(new SmartViewItem("Important", "#D83B01", SmartViewKind.Important));
        SmartViews.Add(new SmartViewItem("Planned", "#5C2D91", SmartViewKind.Planned));
        _selectedSmartView = SmartViews[0];

        Tasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTasks));

        AddTaskCommand = new AsyncRelayCommand(async () => await AddTaskAsync());
        OpenGroupSettingsCommand = new AsyncRelayCommand<Group>(async group => await OpenGroupSettingsAsync(group));
        CreateGroupCommand = new AsyncRelayCommand(async () => await CreateGroupAsync());
        SignOutCommand = new RelayCommand(() => SignOutRequested?.Invoke(this, EventArgs.Empty));
        CloseDetailsCommand = new RelayCommand(() => SelectedTask = null);
        SelectTaskCommand = new RelayCommand<TodoTaskViewModel>(task => { if (task is not null) SelectedTask = task; });
        ToggleTaskStatusCommand = new AsyncRelayCommand<TodoTaskViewModel>(async task => await ToggleTaskStatusAsync(task));
        ReorderTasksCommand = new AsyncRelayCommand<ReorderPayload>(async payload => await ReorderTasksAsync(payload));
    }

    public User User { get; }
    public int CurrentUserId => User.UserID;
    public UserViewModel CurrentUser { get; }

    public ObservableCollection<SmartViewItem> SmartViews { get; } = new();
    public ObservableCollection<Group> Groups { get; } = new();
    public ObservableCollection<TodoTaskViewModel> Tasks { get; } = new();

    public ICommand AddTaskCommand { get; }
    public ICommand OpenGroupSettingsCommand { get; }
    public ICommand CreateGroupCommand { get; }
    public ICommand SignOutCommand { get; }
    public ICommand CloseDetailsCommand { get; }
    public ICommand SelectTaskCommand { get; }
    public AsyncRelayCommand<TodoTaskViewModel> ToggleTaskStatusCommand { get; }
    public AsyncRelayCommand<ReorderPayload> ReorderTasksCommand { get; }

    public event EventHandler<string>? ToastRequested;
    public event EventHandler? SignOutRequested;

    public Group? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (_selectedGroup == value)
                return;
            _selectedGroup = value;
            OnPropertyChanged();

            if (value is not null && _selectedSmartView is not null)
            {
                _selectedSmartView = null;
                OnPropertyChanged(nameof(SelectedSmartView));
            }

            _ = RefreshSourceAndViewAsync();
        }
    }

    public SmartViewItem? SelectedSmartView
    {
        get => _selectedSmartView;
        set
        {
            if (_selectedSmartView == value)
                return;
            _selectedSmartView = value;
            OnPropertyChanged();

            if (value is not null && _selectedGroup is not null)
            {
                _selectedGroup = null;
                OnPropertyChanged(nameof(SelectedGroup));
            }

            _ = RefreshSourceAndViewAsync();
        }
    }

    public TodoTaskViewModel? SelectedTask
    {
        get => _selectedTask;
        set
        {
            if (SetProperty(ref _selectedTask, value))
                OnPropertyChanged(nameof(IsDetailOpen));
        }
    }

    public bool IsDetailOpen => SelectedTask is not null;
    public bool HasTasks => Tasks.Count > 0;

    public bool SortByImportance
    {
        get => _sortByImportance;
        set
        {
            if (SetProperty(ref _sortByImportance, value))
                _ = RebuildViewAsync();
        }
    }

    public bool ShowCompleted
    {
        get => _showCompleted;
        set
        {
            if (SetProperty(ref _showCompleted, value))
                _ = RebuildViewAsync();
        }
    }

    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _ = RebuildViewAsync();
        }
    }

    public string? NewTaskTitle
    {
        get => _newTaskTitle;
        set => SetProperty(ref _newTaskTitle, value);
    }

    public string CurrentViewTitle
    {
        get => _currentViewTitle;
        private set => SetProperty(ref _currentViewTitle, value);
    }

    public string? CurrentViewSubtitle
    {
        get => _currentViewSubtitle;
        private set => SetProperty(ref _currentViewSubtitle, value);
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;
        _initialized = true;

        try
        {
            await PlannedTaskRepository.AutoUpdateAsync();
            await LoadLookupsAsync();
            await RefreshGroupsAsync();
            await RefreshCountsAsync();
            await RefreshSourceAndViewAsync();
        }
        catch (Exception)
        {
            RaiseToast("Could not load your data. Check the database connection.");
        }
    }

    public void RaiseToast(string message) =>
        ToastRequested?.Invoke(this, message);

    public async Task RefreshCountsOnlyAsync() =>
        await RefreshCountsAsync();

    public async Task RefreshAfterMutationAsync() =>
        await RefreshSourceAndViewAsync();

    private async Task LoadLookupsAsync()
    {
        _statusNames.Clear();
        foreach (var status in await TaskRepository.GetTaskStatusesAsync())
            _statusNames[status.TaskStatusID] = status.StatusName;

        _levelNames.Clear();
        foreach (var level in await TaskRepository.GetImportanceLevelsAsync())
            _levelNames[level.LevelOfImportanceID] = level.LevelName;

        _assignees.Clear();
        _assignees.AddRange(await UserRepository.GetAllUsersAsync());

        _recurrenceOptions.Clear();
        foreach (var type in await PlannedTaskRepository.GetRepetitionTypesAsync())
            _recurrenceOptions.Add(new RecurrenceOption(type));
    }

    private async Task RefreshGroupsAsync()
    {
        var groups = await GroupRepository.GetGroupsForUserAsync(CurrentUserId);
        Groups.Clear();
        foreach (var group in groups)
            Groups.Add(group);
    }

    private async Task RefreshCountsAsync()
    {
        SetSmartViewCount(SmartViewKind.All, await TaskRepository.CountParentTasksAsync(CurrentUserId));
        SetSmartViewCount(SmartViewKind.Important, await TaskRepository.CountParentTasksAsync(CurrentUserId, 3));
        SetSmartViewCount(SmartViewKind.Planned, await PlannedTaskRepository.CountPlannedTasksAsync(CurrentUserId, PlannedScope.All));
        SetSmartViewCount(SmartViewKind.Today, await PlannedTaskRepository.CountPlannedTasksAsync(CurrentUserId, PlannedScope.Daily));
    }

    private void SetSmartViewCount(SmartViewKind kind, int count) =>
        SmartViews.First(v => v.Kind == kind).Count = count;

    public async Task RefreshSourceAndViewAsync()
    {
        await LoadSourceAsync();
        await RefreshCountsAsync();
        await RebuildViewAsync();
    }

    private async Task LoadSourceAsync()
    {
        _sourceTasks = new List<TodoTask>();
        _sourcePlanned = new Dictionary<int, PlannedTask>();
        _preservedSelectedTaskId = SelectedTask?.TaskID;

        if (_selectedGroup is not null)
        {
            _sourceTasks.AddRange(await TaskRepository.GetTasksByGroupAsync(_selectedGroup.GroupID));
            CurrentViewTitle = _selectedGroup.GroupName;
            CurrentViewSubtitle = _selectedGroup.GroupDescription;
            return;
        }

        switch (_selectedSmartView?.Kind ?? SmartViewKind.All)
        {
            case SmartViewKind.All:
                _sourceTasks.AddRange(await TaskRepository.GetParentTasksAsync(CurrentUserId, _sortByImportance));
                CurrentViewTitle = "All";
                break;
            case SmartViewKind.Today:
                await LoadPlannedSourceAsync(PlannedScope.Daily);
                CurrentViewTitle = "Today";
                break;
            case SmartViewKind.Planned:
                await LoadPlannedSourceAsync(PlannedScope.All);
                CurrentViewTitle = "Planned";
                break;
            case SmartViewKind.Important:
                _sourceTasks.AddRange(await TaskRepository.GetParentTasksAsync(CurrentUserId, _sortByImportance));
                _sourceTasks = _sourceTasks.Where(t => t.LevelOfImportanceID == 3).ToList();
                CurrentViewTitle = "Important";
                break;
        }
    }

    private async Task LoadPlannedSourceAsync(PlannedScope scope)
    {
        var planned = await PlannedTaskRepository.GetPlannedTasksAsync(CurrentUserId, scope, PlannedSort.Ranking);
        foreach (var plannedTask in planned)
        {
            if (_sourcePlanned.ContainsKey(plannedTask.TaskID) || _sourceTasks.Any(t => t.TaskID == plannedTask.TaskID))
                continue;

            var task = await TaskRepository.GetTaskAsync(plannedTask.TaskID);
            if (task is null)
                continue;

            _sourceTasks.Add(task);
            _sourcePlanned[plannedTask.TaskID] = plannedTask;
        }
    }

    private int _refreshGeneration;

    private async Task RebuildViewAsync()
    {
        var generation = Interlocked.Increment(ref _refreshGeneration);
        _preservedSelectedTaskId ??= SelectedTask?.TaskID;

        IEnumerable<TodoTask> source = _sourceTasks;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText!.Trim();
            source = source.Where(t => t.TaskName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (!ShowCompleted)
            source = source.Where(t => t.TaskStatusID != 3);

        var ordered = SortByImportance
            ? source.OrderByDescending(t => t.LevelOfImportanceID).ThenBy(t => t.Ranking)
            : source.OrderBy(t => t.Ranking).ThenByDescending(t => t.LevelOfImportanceID);

        var built = ordered.ToList();
        var viewModels = new List<TodoTaskViewModel>(built.Count);
        foreach (var task in built)
        {
            var viewModel = new TodoTaskViewModel(task, this, _statusNames, _levelNames, _assignees, _recurrenceOptions);
            _sourcePlanned.TryGetValue(task.TaskID, out var planned);
            viewModel.ApplyPlanned(planned);
            viewModels.Add(viewModel);
        }

        await Task.WhenAll(viewModels.Select(v => v.LoadDetailsAsync()));
        var finalGeneration = Volatile.Read(ref _refreshGeneration);
        if (generation != finalGeneration)
            return;

        Tasks.Clear();
        foreach (var viewModel in viewModels)
            Tasks.Add(viewModel);

        SelectedTask = _preservedSelectedTaskId is int preservedId
            ? Tasks.FirstOrDefault(t => t.TaskID == preservedId)
            : null;

        CurrentViewSubtitle = $"{Tasks.Count} task{(Tasks.Count == 1 ? "" : "s")}";
        OnPropertyChanged(nameof(HasTasks));
    }

    private async Task AddTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTaskTitle))
            return;

        var taskName = NewTaskTitle!.Trim();
        var groupId = _selectedGroup?.GroupID;
        await TaskRepository.AddTaskAsync(null, 2, CurrentUserId, groupId, taskName, 2, null, "#FFFFFF");

        NewTaskTitle = string.Empty;
        await RefreshSourceAndViewAsync();
        RaiseToast("Task added");
    }

    private async Task ToggleTaskStatusAsync(TodoTaskViewModel? task)
    {
        if (task is null)
            return;

        var targetStatusId = task.IsCompleted ? 3 : 2;
        await TaskRepository.UpdateTaskStatusAsync(task.TaskID, targetStatusId);

        if (targetStatusId == 3 && !ShowCompleted)
        {
            if (SelectedTask == task)
                SelectedTask = null;
            Tasks.Remove(task);
        }

        await RefreshCountsAsync();
        RaiseToast(targetStatusId == 3 ? "Task completed" : "Task marked as incomplete");
    }

    private async Task ReorderTasksAsync(ReorderPayload? payload)
    {
        if (payload is null)
            return;

        var dragged = payload.Dragged;
        var target = payload.Target;
        var fromIndex = Tasks.IndexOf(dragged);
        var targetIndex = Tasks.IndexOf(target);
        if (fromIndex < 0 || targetIndex < 0 || fromIndex == targetIndex)
            return;

        Tasks.RemoveAt(fromIndex);
        targetIndex = Tasks.IndexOf(target);
        Tasks.Insert(targetIndex, dragged);

        for (var i = 0; i < Tasks.Count; i++)
            await TaskRepository.UpdateRankingAsync(Tasks[i].TaskID, i + 1);

        RaiseToast("Order updated");
    }

    private async Task OpenGroupSettingsAsync(Group? group)
    {
        if (group is null)
            return;

        var viewModel = new GroupSettingsViewModel(User, group, this);
        var window = new GroupSettingsView { Owner = _window, DataContext = viewModel };
        viewModel.RequestClose += (_, _) => window.Close();
        _ = viewModel.InitializeAsync();
        window.ShowDialog();

        var selectedGroupId = _selectedGroup?.GroupID;
        await RefreshGroupsAsync();
        if (selectedGroupId is int groupId && Groups.FirstOrDefault(g => g.GroupID == groupId) is { } refreshed)
        {
            SelectedGroup = refreshed;
        }
        else
        {
            SelectedGroup = null;
            await RefreshSourceAndViewAsync();
        }
    }

    private async Task CreateGroupAsync()
    {
        var viewModel = new GroupSettingsViewModel(User, null, this);
        var window = new GroupSettingsView { Owner = _window, DataContext = viewModel };
        viewModel.RequestClose += (_, _) => window.Close();
        _ = viewModel.InitializeAsync();
        window.ShowDialog();

        await RefreshGroupsAsync();
        SelectedGroup = null;
        await RefreshSourceAndViewAsync();
    }
}