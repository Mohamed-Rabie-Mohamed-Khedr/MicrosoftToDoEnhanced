using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Core.Repositories;
using Microsoft.Win32;

namespace MicrosoftToDoEnhanced.ViewModels;

public class TodoTaskViewModel : ViewModelBase
{
    private readonly MainViewModel _owner;
    private readonly TodoTask _model;
    private readonly Dictionary<int, string> _statusNames;
    private readonly Dictionary<int, string> _levelNames;
    private readonly List<RecurrenceOption> _recurrenceOptions;

    private string _taskName;
    private string? _description;
    private string? _stepsSummary;
    private DateTime? _dueDate;
    private DateTime? _endDate;
    private RecurrenceOption? _selectedRecurrence;
    private User? _selectedAssignee;
    private string? _newStepTitle;
    private PlannedTask? _planned;
    private bool _detailsLoaded;

    public TodoTaskViewModel(
        TodoTask model,
        MainViewModel owner,
        Dictionary<int, string> statusNames,
        Dictionary<int, string> levelNames,
        List<User> assignees,
        List<RecurrenceOption> recurrenceOptions)
    {
        _model = model;
        _owner = owner;
        _statusNames = statusNames;
        _levelNames = levelNames;
        Assignees = assignees;
        _recurrenceOptions = recurrenceOptions;
        _taskName = model.TaskName;
        _description = model.Description;
        _selectedRecurrence = _recurrenceOptions.FirstOrDefault(o => o.Type.RepetitionTypeID == 1);

        AddStepCommand = new AsyncRelayCommand(AddStepAsync, onError: OnAsyncCommandError);
        ToggleStepCommand = new AsyncRelayCommand<TodoTaskViewModel>(ToggleStepAsync, onError: OnAsyncCommandError);
        RemoveStepCommand = new AsyncRelayCommand<TodoTaskViewModel>(RemoveStepAsync, onError: OnAsyncCommandError);
        AddAttachmentCommand = new AsyncRelayCommand(AddAttachmentAsync, onError: OnAsyncCommandError);
        RemoveAttachmentCommand = new AsyncRelayCommand<Attachment>(RemoveAttachmentAsync, onError: OnAsyncCommandError);
        DeleteTaskCommand = new AsyncRelayCommand(DeleteAsync, onError: OnAsyncCommandError);
        SaveTaskCommand = new AsyncRelayCommand(SaveAsync, onError: OnAsyncCommandError);
        OpenStepCommand = new RelayCommand<TodoTaskViewModel>(OpenStep);
        ToggleExpandCommand = new RelayCommand(ToggleExpand);
    }

    private void OnAsyncCommandError(Exception exception) =>
        _owner.RaiseToast(DescribeError(exception));

    public int TaskID => _model.TaskID;
    public int? TaskParentID => _model.TaskParentID;
    public bool IsStep => _model.TaskParentID is not null;
    public DateTime CreationDate => _model.CreationDate;
    public int Ranking => _model.Ranking;

    public bool HasChildren => Steps.Count > 0;

    private bool _isExpanded = false;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    internal void SetRanking(int ranking) => _model.Ranking = ranking;

    internal void SetChildren(IEnumerable<TodoTaskViewModel> children)
    {
        Steps.Clear();
        foreach (var child in children)
            Steps.Add(child);

        UpdateStepsSummary();
        OnPropertyChanged(nameof(HasChildren));
    }

    public string TaskName
    {
        get => _taskName;
        set
        {
            if (SetProperty(ref _taskName, value))
            {
                _model.TaskName = value;
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public string? Description
    {
        get => _description;
        set
        {
            if (SetProperty(ref _description, value))
                _model.Description = value;
        }
    }

    public string Title => TaskName;

    public string Color => _model.Color;

    public bool IsCompleted
    {
        get => _model.TaskStatusID == (int)TaskState.Completed;
        set
        {
            if (IsCompleted == value)
                return;
            _model.TaskStatusID = value ? (int)TaskState.Completed : (int)TaskState.Incomplete;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusName));
        }
    }

    public string StatusName =>
        _statusNames.TryGetValue(_model.TaskStatusID, out var name) ? name : "Incomplete";

    public string? LevelName =>
        _levelNames.GetValueOrDefault(_model.LevelOfImportanceID);

    private bool _canReorder = true;

    public bool CanReorder
    {
        get => _canReorder;
        set => SetProperty(ref _canReorder, value);
    }

    public int LevelOfImportanceId
    {
        get => _model.LevelOfImportanceID;
        set
        {
            if (_model.LevelOfImportanceID == value)
                return;
            _model.LevelOfImportanceID = value;
            OnPropertyChanged(nameof(LevelOfImportanceId));
            OnPropertyChanged(nameof(LevelName));
            OnPropertyChanged(nameof(IsLow));
            OnPropertyChanged(nameof(IsMedium));
            OnPropertyChanged(nameof(IsHigh));
        }
    }

    public bool IsLow
    {
        get => LevelOfImportanceId == (int)TaskImportance.Low;
        set
        {
            if (value)
                LevelOfImportanceId = (int)TaskImportance.Low;
            else if (IsLow)
                OnPropertyChanged();
        }
    }

    public bool IsMedium
    {
        get => LevelOfImportanceId == (int)TaskImportance.Medium;
        set
        {
            if (value)
                LevelOfImportanceId = (int)TaskImportance.Medium;
            else if (IsMedium)
                OnPropertyChanged();
        }
    }

    public bool IsHigh
    {
        get => LevelOfImportanceId == (int)TaskImportance.High;
        set
        {
            if (value)
                LevelOfImportanceId = (int)TaskImportance.High;
            else if (IsHigh)
                OnPropertyChanged();
        }
    }

    public ObservableCollection<TodoTaskViewModel> Steps { get; } = new();

    public string? StepsSummary
    {
        get => _stepsSummary;
        private set => SetProperty(ref _stepsSummary, value);
    }

    public ObservableCollection<Attachment> Attachments { get; } = new();

    private bool _hasAttachments;

    public bool HasAttachments
    {
        get => _hasAttachments || Attachments.Count > 0;
        private set => SetProperty(ref _hasAttachments, value);
    }

    public DateTime? DueDate
    {
        get => _dueDate;
        set
        {
            if (!SetProperty(ref _dueDate, value))
                return;

            OnPropertyChanged(nameof(DueDateDisplay));

            if (value is null && SelectedRecurrence?.Type.RepetitionTypeID > 1)
                SelectedRecurrence = _recurrenceOptions.FirstOrDefault(o => o.Type.RepetitionTypeID == 1);
        }
    }

    public DateTime? EndDate
    {
        get => _endDate;
        set
        {
            if (SetProperty(ref _endDate, value))
                OnPropertyChanged(nameof(DueDateDisplay));
        }
    }

    public string? DueDateDisplay
    {
        get
        {
            if (_dueDate is DateTime start)
                return _endDate is DateTime end && end.Date != start.Date
                    ? $"{start:ddd, MMM d} – {end:ddd, MMM d}"
                    : $"{start:ddd, MMM d}";
            return _endDate is DateTime onlyEnd ? $"{onlyEnd:ddd, MMM d}" : null;
        }
    }

    public List<RecurrenceOption> RecurrenceOptions => _recurrenceOptions;

    public RecurrenceOption? SelectedRecurrence
    {
        get => _selectedRecurrence;
        set
        {
            if (!SetProperty(ref _selectedRecurrence, value))
                return;

            OnPropertyChanged(nameof(RecurrenceName));

            if (value?.Type.RepetitionTypeID > 1 && _dueDate is null)
                DueDate = DateTime.Today;
        }
    }

    public string? RecurrenceName =>
        _selectedRecurrence is { Type.RepetitionTypeID: > 1 } option ? option.Name : null;

    public List<User> Assignees { get; }

    public User? SelectedAssignee
    {
        get => _selectedAssignee;
        set
        {
            if (SetProperty(ref _selectedAssignee, value))
            {
                OnPropertyChanged(nameof(AssignedToName));
                OnPropertyChanged(nameof(AssignedToColor));
            }
        }
    }

    public string? AssignedToName => _selectedAssignee?.ShowName;
    public string? AssignedToColor => _selectedAssignee?.Color;

    public string? NewStepTitle
    {
        get => _newStepTitle;
        set => SetProperty(ref _newStepTitle, value);
    }

    public ICommand AddStepCommand { get; }
    public ICommand ToggleStepCommand { get; }
    public ICommand RemoveStepCommand { get; }
    public ICommand AddAttachmentCommand { get; }
    public ICommand RemoveAttachmentCommand { get; }
    public ICommand DeleteTaskCommand { get; }
    public ICommand SaveTaskCommand { get; }
    public ICommand OpenStepCommand { get; }
    public ICommand ToggleExpandCommand { get; }

    private void OpenStep(TodoTaskViewModel? step)
    {
        if (step is not null)
            _owner.OpenDetailsForStep(step, TaskID);
    }

    private void ToggleExpand()
    {
        if (!string.IsNullOrWhiteSpace(_owner.SearchText))
            return;

        IsExpanded = !IsExpanded;
        _owner.RecordExpand(TaskID, IsExpanded);
        _owner.ApplyExpand(this);
    }

    public async Task LoadDetailsAsync()
    {
        if (_detailsLoaded)
            return;
        _detailsLoaded = true;

        if (_planned is null)
            ApplyPlanned(await PlannedTaskRepository.GetPlannedAsync(TaskID));

        await ReloadAttachmentsAsync();
        await LoadAssignedAsync();
    }

    public void ApplyPlanned(PlannedTask? planned)
    {
        if (planned is null)
            return;

        _planned = planned;
        DueDate = planned.PlannedStartDate;
        EndDate = planned.PlannedEndDate;
        ApplyRecurrence(planned.RepetitionTypeID);
    }

    public void ApplyListExtras(TaskListExtras extras)
    {
        StepsSummary = extras.SubtaskCount > 0
            ? $"{extras.SubtaskCompletedCount}/{extras.SubtaskCount}"
            : null;

        HasAttachments = extras.HasAttachments;

        if (extras.DueStartDate is DateTime dueDate)
            DueDate = dueDate;
        EndDate = extras.DueEndDate;

        ApplyRecurrence(extras.RepetitionTypeID);

        if (extras.AssignedToUserID is int assignedUserId && extras.AssignedToName is not null)
        {
            SelectedAssignee = Assignees.FirstOrDefault(u => u.UserID == assignedUserId)
                ?? new User
                {
                    UserID = assignedUserId,
                    ShowName = extras.AssignedToName,
                    Color = extras.AssignedToColor ?? string.Empty
                };
        }
    }

    private void ApplyRecurrence(int? repetitionTypeId)
    {
        if (repetitionTypeId is int id)
            SelectedRecurrence = _recurrenceOptions.FirstOrDefault(o => o.Type.RepetitionTypeID == id);
    }

    internal static async Task<TaskStatusResult?> ToggleStatusAsync(
        TodoTaskViewModel task, int requested, int actingUserId, Action<string> toast, string forcedMessage)
    {
        var priorIsCompleted = task.IsCompleted;

        TaskStatusResult result;
        try
        {
            result = await TaskRepository.UpdateTaskStatusAsync(task.TaskID, requested, actingUserId);
        }
        catch
        {
            task.IsCompleted = priorIsCompleted;
            throw;
        }

        if (result.StatusForced)
        {
            task.IsCompleted = priorIsCompleted;
            toast(forcedMessage);
            return null;
        }

        task.IsCompleted = result.TaskStatusID == (int)TaskState.Completed;
        return result;
    }

    internal async Task RefreshChildrenAsync()
    {
        var children = await TaskRepository.GetChildTasksAsync(TaskID, _owner.CurrentUserId);

        var existing = Steps.ToDictionary(c => c.TaskID);
        var rebuilt = new List<TodoTaskViewModel>(children.Count);
        foreach (var child in children)
        {
            rebuilt.Add(existing.TryGetValue(child.TaskID, out var vm)
                ? vm
                : new TodoTaskViewModel(child, _owner, _statusNames, _levelNames, Assignees, _recurrenceOptions));
        }

        var changed = rebuilt.Count != Steps.Count;
        if (!changed)
        {
            for (var i = 0; i < rebuilt.Count; i++)
            {
                if (!ReferenceEquals(rebuilt[i], Steps[i]))
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
        {
            UpdateStepsSummary();
            return;
        }

        SetChildren(rebuilt);
        _owner.RebuildView();
    }

    private async Task ReloadAttachmentsAsync()
    {
        var attachments = await AttachmentRepository.GetAttachmentInfosAsync(new[] { TaskID }, _owner.CurrentUserId);

        Attachments.Clear();
        foreach (var attachment in attachments)
            Attachments.Add(attachment);

        OnPropertyChanged(nameof(HasAttachments));
    }

    private async Task LoadAssignedAsync()
    {
        var assignee = await AssignedTaskRepository.GetAssignedUserAsync(TaskID, _owner.CurrentUserId);
        if (assignee is null)
            return;

        SelectedAssignee = Assignees.FirstOrDefault(u => u.UserID == assignee.UserID) ?? assignee;
    }

    internal void UpdateStepsSummary()
    {
        var total = Steps.Count;
        var completed = Steps.Count(s => s.IsCompleted);
        StepsSummary = total == 0 ? null : $"{completed}/{total}";
    }

    private async Task AddStepAsync()
    {
        if (string.IsNullOrWhiteSpace(NewStepTitle))
        {
            _owner.RaiseToast("Type a step first.");
            return;
        }

        await TaskRepository.AddTaskAsync(
            TaskID,
            (int)TaskState.Incomplete,
            _owner.CurrentUserId,
            _model.GroupID,
            NewStepTitle.Trim(),
            _model.LevelOfImportanceID,
            null,
            _model.Color);

        NewStepTitle = string.Empty;
        await RefreshChildrenAsync();
        _owner.RaiseToast("Step added");
        await _owner.RefreshCountsAsync();
    }

    private async Task ToggleStepAsync(TodoTaskViewModel? step)
    {
        if (step is null)
            return;

        var requested = step.IsCompleted ? (int)TaskState.Incomplete : (int)TaskState.Completed;
        var priorDueDate = step.DueDate;

        var result = await ToggleStatusAsync(
            step, requested, _owner.CurrentUserId, _owner.RaiseToast, "The step could not be changed as requested.");
        if (result is null)
            return;

        DateTime? advancedTo = requested == (int)TaskState.Completed
            && result.PlannedStartDate is DateTime nextOccurrence
            && nextOccurrence != priorDueDate
            ? nextOccurrence
            : null;

        if (result.PlannedStartDate is DateTime nextStart)
            step.DueDate = nextStart;
        step.EndDate = result.PlannedEndDate;

        UpdateStepsSummary();
        _owner.RebuildView();
        await _owner.RefreshCountsAsync();

        if (advancedTo is DateTime next)
            _owner.RaiseToast($"Next occurrence: {next:d}");
        else
            _owner.RaiseToast(step.IsCompleted ? "Step completed" : "Step marked incomplete");
    }

    private async Task RemoveStepAsync(TodoTaskViewModel? step)
    {
        if (step is null)
            return;

        await TaskRepository.DeleteTaskAsync(step.TaskID, _owner.CurrentUserId);
        Steps.Remove(step);
        UpdateStepsSummary();
        OnPropertyChanged(nameof(HasChildren));
        _owner.RebuildView();
        _owner.RaiseToast("Step removed");
        await _owner.RefreshCountsAsync();
    }

    private async Task AddAttachmentAsync()
    {
        var dialog = new OpenFileDialog { Multiselect = false };
        if (dialog.ShowDialog() != true)
            return;

        var fileBytes = await File.ReadAllBytesAsync(dialog.FileName);
        var fileSizeKB = (int)Math.Ceiling(fileBytes.Length / 1024d);

        await AttachmentRepository.AddAttachmentAsync(
            TaskID, Path.GetFileName(dialog.FileName), fileBytes, fileSizeKB, _owner.CurrentUserId);

        await ReloadAttachmentsAsync();
        _owner.RaiseToast("Attachment added");
    }

    private async Task RemoveAttachmentAsync(Attachment? attachment)
    {
        if (attachment is null)
            return;

        await AttachmentRepository.DeleteAttachmentAsync(attachment.AttachmentID, _owner.CurrentUserId);
        Attachments.Remove(attachment);
        OnPropertyChanged(nameof(HasAttachments));
        _owner.RaiseToast("Attachment removed");
    }

    private async Task DeleteAsync()
    {
        await TaskRepository.DeleteTaskAsync(TaskID, _owner.CurrentUserId);
        _owner.CloseDetailsIfOpen(TaskID);
        _owner.RaiseToast("Task deleted");
        await _owner.RefreshSourceAndViewAsync();
    }

    private async Task SaveAsync()
    {
        await TaskRepository.SaveTaskAsync(
            TaskID,
            _model.TaskParentID,
            _model.TaskStatusID,
            _model.UserID,
            _model.GroupID,
            TaskName,
            LevelOfImportanceId,
            Description,
            _model.Color,
            DueDate,
            EndDate,
            SelectedRecurrence?.Type.RepetitionTypeID ?? 1,
            SelectedAssignee?.UserID);

        _owner.RaiseToast("Task saved");
        await _owner.RefreshSourceAndViewAsync();
    }
}
