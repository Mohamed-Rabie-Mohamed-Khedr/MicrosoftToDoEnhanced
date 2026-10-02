using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Core.Repositories;
using MicrosoftToDoEnhanced.Services;

namespace MicrosoftToDoEnhanced.ViewModels;

public sealed record ReorderPayload(TodoTaskViewModel Dragged, TodoTaskViewModel Target);

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
    private string? _searchText;
    private string? _newTaskTitle;
    private string _currentViewTitle = "All";
    private string? _currentViewSubtitle;
    private int? _preservedSelectedTaskId;
    private List<TodoTask> _sourceTasks = new();
    private Dictionary<int, PlannedTask> _sourcePlanned = new();
    private Dictionary<int, TaskListExtras> _sourceExtras = new();
    private List<TodoTaskViewModel> _sourceViewModels = new();
    private CancellationTokenSource? _searchDebounceCts;
    private bool _initialized;

    public MainViewModel(User user, Window window)
    {
        User = user;
        CurrentUser = user;
        _window = window;

        SmartViews.Add(new SmartViewItem("All", "#707070", SmartViewKind.All));
        SmartViews.Add(new SmartViewItem("Today", "#0078D4", SmartViewKind.Today));
        SmartViews.Add(new SmartViewItem("Important", "#D83B01", SmartViewKind.Important));
        SmartViews.Add(new SmartViewItem("Planned", "#5C2D91", SmartViewKind.Planned));
        _selectedSmartView = SmartViews[0];

        Tasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTasks));

        AddTaskCommand = new AsyncRelayCommand(AddTaskAsync, onError: OnAsyncCommandError);
        OpenGroupSettingsCommand = new AsyncRelayCommand<Group>(OpenGroupSettingsAsync, onError: OnAsyncCommandError);
        CreateGroupCommand = new AsyncRelayCommand(CreateGroupAsync, onError: OnAsyncCommandError);
        SignOutCommand = new RelayCommand(() => SignOutRequested?.Invoke(this, EventArgs.Empty));
        CloseDetailsCommand = new RelayCommand(() => SelectedTask = null);
        ToggleTaskStatusCommand = new AsyncRelayCommand<TodoTaskViewModel>(ToggleTaskStatusAsync, onError: OnAsyncCommandError);
        ReorderTasksCommand = new AsyncRelayCommand<ReorderPayload>(ReorderTasksAsync, onError: OnAsyncCommandError);
    }

    private void OnAsyncCommandError(Exception exception) => RaiseToast(DescribeError(exception));

    public User User { get; }
    public int CurrentUserId => User.UserID;
    public User CurrentUser { get; }

    public ObservableCollection<SmartViewItem> SmartViews { get; } = new();
    public ObservableCollection<Group> Groups { get; } = new();
    public ObservableCollection<TodoTaskViewModel> Tasks { get; } = new();

    public ICommand AddTaskCommand { get; }
    public ICommand OpenGroupSettingsCommand { get; }
    public ICommand CreateGroupCommand { get; }
    public ICommand SignOutCommand { get; }
    public ICommand CloseDetailsCommand { get; }
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

            RefreshSourceAndViewAsync().SafeFireAndForget(OnAsyncCommandError);
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

            RefreshSourceAndViewAsync().SafeFireAndForget(OnAsyncCommandError);
        }
    }

    public TodoTaskViewModel? SelectedTask
    {
        get => _selectedTask;
        set
        {
            if (SetProperty(ref _selectedTask, value))
            {
                OnPropertyChanged(nameof(IsDetailOpen));

                if (value is not null)
                    value.LoadDetailsAsync().SafeFireAndForget(OnAsyncCommandError);
            }
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
                RebuildView();
        }
    }

    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value))
                return;

            _searchDebounceCts?.Cancel();
            var cts = new CancellationTokenSource();
            _searchDebounceCts = cts;
            RebuildViewDebouncedAsync(cts).SafeFireAndForget(OnAsyncCommandError);
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
            await RefreshSourceAndViewAsync();
        }
        catch (Exception)
        {
            RaiseToast("Could not load your data. Check the database connection.");
        }
    }

    public void RaiseToast(string message) =>
        ToastRequested?.Invoke(this, message);

    private async Task LoadLookupsAsync()
    {
        var lookups = await TaskRepository.GetLookupsAsync();

        _statusNames.Clear();
        foreach (var status in lookups.Statuses)
            _statusNames[status.TaskStatusID] = status.StatusName;

        _levelNames.Clear();
        foreach (var level in lookups.Levels)
            _levelNames[level.LevelOfImportanceID] = level.LevelName;

        _assignees.Clear();
        _assignees.AddRange(await UserRepository.GetAllUsersAsync());

        _recurrenceOptions.Clear();
        foreach (var type in lookups.RepetitionTypes)
            _recurrenceOptions.Add(new RecurrenceOption(type));
    }

    private async Task RefreshGroupsAsync()
    {
        var groups = await GroupRepository.GetGroupsForUserAsync(CurrentUserId);
        Groups.Clear();
        foreach (var group in groups)
            Groups.Add(group);
    }

    public async Task RefreshCountsAsync()
    {
        var counts = await TaskRepository.GetTaskCountsAsync(CurrentUserId, includeCompleted: true);

        SetSmartViewCount(SmartViewKind.All, counts.AllCount);
        SetSmartViewCount(SmartViewKind.Important, counts.ImportantCount);
        SetSmartViewCount(SmartViewKind.Planned, counts.PlannedCount);
        SetSmartViewCount(SmartViewKind.Today, counts.TodayCount);
    }

    private void SetSmartViewCount(SmartViewKind kind, int count) =>
        SmartViews.First(v => v.Kind == kind).Count = count;

    public async Task RefreshSourceAndViewAsync()
    {
        await LoadSourceAsync();
        await RefreshCountsAsync();
        RebuildView();
    }

    public bool CanReorderTasks =>
        _selectedGroup is not null
        || _selectedSmartView?.Kind is SmartViewKind.All or null;

    private async Task LoadSourceAsync()
    {
        _sourceTasks = new List<TodoTask>();
        _sourcePlanned = new Dictionary<int, PlannedTask>();
        _sourceExtras = new Dictionary<int, TaskListExtras>();
        _preservedSelectedTaskId = SelectedTask?.TaskID;

        if (_selectedGroup is not null)
        {
            _sourceTasks.AddRange(await TaskRepository.GetGroupParentTasksAsync(_selectedGroup.GroupID, CurrentUserId));
            CurrentViewTitle = _selectedGroup.GroupName;
            CurrentViewSubtitle = _selectedGroup.GroupDescription;
        }
        else
        {
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
                    _sourceTasks = _sourceTasks.Where(t => t.LevelOfImportanceID == (int)TaskImportance.High).ToList();
                    CurrentViewTitle = "Important";
                    break;
            }
        }

        if (_sourceTasks.Count > 0)
        {
            var sourceIds = _sourceTasks.Select(t => t.TaskID).ToList();
            foreach (var extras in await TaskRepository.GetTaskListExtrasAsync(sourceIds, CurrentUserId))
                _sourceExtras[extras.TaskID] = extras;
        }

        BuildSourceViewModels();
    }

    private void BuildSourceViewModels()
    {
        var built = new List<TodoTaskViewModel>(_sourceTasks.Count);
        foreach (var task in _sourceTasks)
        {
            var viewModel = new TodoTaskViewModel(task, this, _statusNames, _levelNames, _assignees, _recurrenceOptions);

            if (_sourcePlanned.TryGetValue(task.TaskID, out var planned))
                viewModel.ApplyPlanned(planned);
            if (_sourceExtras.TryGetValue(task.TaskID, out var extras))
                viewModel.ApplyListExtras(extras);

            built.Add(viewModel);
        }

        _sourceViewModels = built;
    }

    private async Task LoadPlannedSourceAsync(PlannedScope scope)
    {
        var tasks = await PlannedTaskRepository.GetTasksAsync(CurrentUserId, scope);
        if (tasks.Count == 0)
            return;

        var seen = new HashSet<int>(_sourceTasks.Select(t => t.TaskID));
        var pending = tasks
            .Where(t => seen.Add(t.TaskID))
            .ToList();

        var plannedByTask = await PlannedTaskRepository.GetPlannedForTasksAsync(
            pending.Select(t => t.TaskID).ToList());

        foreach (var task in pending)
        {
            if (!plannedByTask.TryGetValue(task.TaskID, out var planned))
                continue;

            _sourceTasks.Add(task);
            _sourcePlanned[task.TaskID] = planned;
        }
    }

    public void RebuildView()
    {
        _preservedSelectedTaskId = SelectedTask?.TaskID;

        IEnumerable<TodoTaskViewModel> source = _sourceViewModels;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText!.Trim();
            source = source.Where(t => t.TaskName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = SortByImportance
            ? source.OrderByDescending(t => t.LevelOfImportanceId).ThenBy(t => t.Ranking)
            : source.OrderBy(t => t.Ranking).ThenByDescending(t => t.LevelOfImportanceId);

        var viewModels = ordered.ToList();

        Tasks.Clear();
        foreach (var viewModel in viewModels)
        {
            viewModel.CanReorder = CanReorderTasks;
            Tasks.Add(viewModel);
        }

        SelectedTask = _preservedSelectedTaskId is int preservedId
            ? viewModels.FirstOrDefault(t => t.TaskID == preservedId)
            : null;

        CurrentViewSubtitle = $"{Tasks.Count} task{(Tasks.Count == 1 ? "" : "s")}";
        OnPropertyChanged(nameof(HasTasks));
    }

    private async Task RebuildViewDebouncedAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(250, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested || !ReferenceEquals(_searchDebounceCts, cts))
            return;

        RebuildView();
    }

    private async Task AddTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTaskTitle))
            return;

        var taskName = NewTaskTitle!.Trim();
        var groupId = _selectedGroup?.GroupID;
        var smartView = _selectedSmartView?.Kind ?? SmartViewKind.All;

        var importance = smartView == SmartViewKind.Important
            ? (int)TaskImportance.High
            : (int)TaskImportance.Medium;

        var taskId = await TaskRepository.AddTaskAsync(
            null,
            (int)TaskState.Incomplete,
            CurrentUserId,
            groupId,
            taskName,
            importance,
            null,
            "#FFFFFF",
            CurrentUserId);

        if (smartView is SmartViewKind.Today or SmartViewKind.Planned)
        {
            await PlannedTaskRepository.AddAsync(
                taskId, DateTime.Today, null, (int)RepetitionPeriod.None, CurrentUserId);
        }

        NewTaskTitle = string.Empty;
        await RefreshSourceAndViewAsync();
        RaiseToast("Task added");
    }

    private async Task ToggleTaskStatusAsync(TodoTaskViewModel? task)
    {
        if (task is null)
            return;

        var requested = task.IsCompleted ? (int)TaskState.Completed : (int)TaskState.Incomplete;
        var priorDueDate = task.DueDate;

        var result = await TodoTaskViewModel.ToggleStatusAsync(
            task, requested, CurrentUserId, RaiseToast, "The task could not be changed as requested.");
        if (result is null)
            return;

        DateTime? advancedTo = requested == (int)TaskState.Completed
            && result.PlannedStartDate is DateTime nextOccurrence
            && nextOccurrence != priorDueDate
            ? nextOccurrence
            : null;

        if (result.PlannedStartDate is DateTime nextStart)
            task.DueDate = nextStart;
        task.EndDate = result.PlannedEndDate;

        RebuildView();
        await RefreshCountsAsync();

        if (advancedTo is DateTime next)
            RaiseToast($"Next occurrence: {next:d}");
        else
            RaiseToast(requested == (int)TaskState.Completed ? "Task completed" : "Task marked as incomplete");
    }

    private async Task ReorderTasksAsync(ReorderPayload? payload)
    {
        if (payload is null)
            return;

        if (!CanReorderTasks)
        {
            RaiseToast("Tasks in this view are ordered by date or importance and cannot be reordered.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            RaiseToast("Clear the search box before reordering");
            return;
        }

        if (SortByImportance)
        {
            RaiseToast("Switch back to \u201CMy order\u201D before reordering");
            return;
        }

        var dragged = payload.Dragged;
        var target = payload.Target;
        var fromIndex = Tasks.IndexOf(dragged);
        var targetIndex = Tasks.IndexOf(target);

        if (fromIndex < 0 || targetIndex < 0)
        {
            RaiseToast("The list changed while you were dragging. Try again.");
            return;
        }

        if (fromIndex == targetIndex)
        {
            RaiseToast("That task is already in that position.");
            return;
        }

        var fullFrom = _sourceTasks.FindIndex(t => t.TaskID == dragged.TaskID);
        var insertAt = _sourceTasks.FindIndex(t => t.TaskID == target.TaskID);
        if (fullFrom < 0 || insertAt < 0)
        {
            RaiseToast("That task is not part of the current list, so it cannot be reordered.");
            return;
        }

        var draggedTask = _sourceTasks[fullFrom];
        var draggedViewModel = _sourceViewModels[fullFrom];

        var sourceTasksSnapshot = _sourceTasks.ToList();
        var sourceViewModelsSnapshot = _sourceViewModels.ToList();

        try
        {
            var movingDown = targetIndex > fromIndex;

            Tasks.RemoveAt(fromIndex);
            Tasks.Insert(Tasks.IndexOf(target) + (movingDown ? 1 : 0), dragged);

            _sourceTasks.RemoveAt(fullFrom);
            _sourceViewModels.RemoveAt(fullFrom);

            var destination = _sourceTasks.FindIndex(t => t.TaskID == target.TaskID) + (movingDown ? 1 : 0);

            _sourceTasks.Insert(destination, draggedTask);
            _sourceViewModels.Insert(destination, draggedViewModel);

            for (var i = 0; i < _sourceTasks.Count; i++)
                _sourceTasks[i].Ranking = i + 1;

            var fullOrder = _sourceTasks.Select(t => t.TaskID).ToList();
            await TaskRepository.ReorderTasksAsync(
                CurrentUserId, _selectedGroup?.GroupID, fullOrder);
        }
        catch
        {
            _sourceTasks = sourceTasksSnapshot;
            _sourceViewModels = sourceViewModelsSnapshot;
            for (var i = 0; i < _sourceTasks.Count; i++)
                _sourceTasks[i].Ranking = i + 1;
            RebuildView();
            throw;
        }

        RaiseToast("Order updated");
    }

    private async Task OpenGroupSettingsAsync(Group? group)
    {
        if (group is null)
            return;

        var dialogGroupId = await GroupDialogService.ShowAsync(_window, this, group);

        await SelectGroupOrResetAsync(dialogGroupId ?? _selectedGroup?.GroupID);
    }

    private async Task CreateGroupAsync()
    {
        var createdGroupId = await GroupDialogService.ShowAsync(_window, this);

        await SelectGroupOrResetAsync(createdGroupId);
    }

    private async Task SelectGroupOrResetAsync(int? groupId)
    {
        await RefreshGroupsAsync();
        if (groupId is int id && Groups.FirstOrDefault(g => g.GroupID == id) is { } group)
        {
            SelectedGroup = group;
        }
        else
        {
            SelectedGroup = null;
            await RefreshSourceAndViewAsync();
        }
    }
}
