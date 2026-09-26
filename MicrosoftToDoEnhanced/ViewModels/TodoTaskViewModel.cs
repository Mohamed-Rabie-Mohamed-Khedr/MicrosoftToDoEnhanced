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
    private int? _assignedId;
    private int? _assignedToUserId;
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

        AddStepCommand = new AsyncRelayCommand(async () => await AddStepAsync());
        ToggleStepCommand = new AsyncRelayCommand<TodoTaskViewModel>(async step => await ToggleStepAsync(step));
        RemoveStepCommand = new AsyncRelayCommand<TodoTaskViewModel>(async step => await RemoveStepAsync(step));
        AddAttachmentCommand = new AsyncRelayCommand(async () => await AddAttachmentAsync());
        RemoveAttachmentCommand = new AsyncRelayCommand<Attachment>(async attachment => await RemoveAttachmentAsync(attachment));
        DeleteTaskCommand = new AsyncRelayCommand(async () => await DeleteAsync());
        SaveTaskCommand = new AsyncRelayCommand(async () => await SaveAsync());
    }

    public TodoTask Model => _model;

    public int TaskID => _model.TaskID;
    public int? TaskParentID => _model.TaskParentID;
    public DateTime CreationDate => _model.CreationDate;
    public int Ranking => _model.Ranking;

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

    public string Color
    {
        get => _model.Color ?? "#FFFFFF";
        set => _model.Color = value;
    }

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

    public string? LevelColor => null;

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
        set { if (value) LevelOfImportanceId = (int)TaskImportance.Low; }
    }

    public bool IsMedium
    {
        get => LevelOfImportanceId == (int)TaskImportance.Medium;
        set { if (value) LevelOfImportanceId = (int)TaskImportance.Medium; }
    }

    public bool IsHigh
    {
        get => LevelOfImportanceId == (int)TaskImportance.High;
        set { if (value) LevelOfImportanceId = (int)TaskImportance.High; }
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
        set => SetProperty(ref _dueDate, value);
    }

    public DateTime? EndDate
    {
        get => _endDate;
        set => SetProperty(ref _endDate, value);
    }

    public List<RecurrenceOption> RecurrenceOptions => _recurrenceOptions;

    public RecurrenceOption? SelectedRecurrence
    {
        get => _selectedRecurrence;
        set
        {
            if (SetProperty(ref _selectedRecurrence, value))
                OnPropertyChanged(nameof(RecurrenceName));
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

    public async Task LoadDetailsAsync()
    {
        if (_detailsLoaded)
            return;
        _detailsLoaded = true;

        if (_planned is null)
            ApplyPlanned(await PlannedTaskRepository.GetPlannedAsync(TaskID));

        await ReloadStepsAsync();
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
        if (planned.RepetitionTypeID is int repetitionTypeId)
        {
            SelectedRecurrence =
                _recurrenceOptions.FirstOrDefault(o => o.Type.RepetitionTypeID == repetitionTypeId);
        }
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

        if (extras.RepetitionTypeID is int repetitionTypeId)
        {
            SelectedRecurrence =
                _recurrenceOptions.FirstOrDefault(o => o.Type.RepetitionTypeID == repetitionTypeId);
        }

        if (extras.AssignedToUserID is int assignedUserId && extras.AssignedToName is not null)
        {
            _assignedToUserId = assignedUserId;
            SelectedAssignee = Assignees.FirstOrDefault(u => u.UserID == assignedUserId)
                ?? new User
                {
                    UserID = assignedUserId,
                    ShowName = extras.AssignedToName,
                    Color = extras.AssignedToColor ?? string.Empty
                };
        }
    }

    private async Task ReloadStepsAsync()
    {
        var children = await TaskRepository.GetChildTasksAsync(TaskID, _owner.CurrentUserId);

        Steps.Clear();
        foreach (var child in children)
            Steps.Add(new TodoTaskViewModel(child, _owner, _statusNames, _levelNames, Assignees, _recurrenceOptions));

        UpdateStepsSummary();
    }

    private async Task ReloadAttachmentsAsync()
    {
        var attachments = await AttachmentRepository.GetAttachmentInfosAsync(new[] { TaskID });

        Attachments.Clear();
        foreach (var attachment in attachments)
            Attachments.Add(attachment);

        OnPropertyChanged(nameof(HasAttachments));
    }

    private async Task LoadAssignedAsync()
    {
        var assigned = await AssignedTaskRepository.GetByTaskAsync(TaskID);
        if (assigned.Count == 0)
            return;

        _assignedId = assigned[0].AssignedID;
        var assignee = await AssignedTaskRepository.GetAssignedUserAsync(TaskID);
        if (assignee is null)
            return;

        _assignedToUserId = assignee.UserID;
        SelectedAssignee = Assignees.FirstOrDefault(u => u.UserID == assignee.UserID) ?? assignee;
    }

    private void UpdateStepsSummary()
    {
        var total = Steps.Count;
        var completed = Steps.Count(s => s.IsCompleted);
        StepsSummary = total == 0 ? null : $"{completed}/{total}";
    }

    private async Task AddStepAsync()
    {
        if (string.IsNullOrWhiteSpace(NewStepTitle))
            return;

        await TaskRepository.AddTaskAsync(
            TaskID,
            (int)TaskState.Incomplete,
            _owner.CurrentUserId,
            _model.GroupID,
            NewStepTitle.Trim(),
            _model.LevelOfImportanceID,
            null,
            _model.Color ?? "#FFFFFF",
            _owner.CurrentUserId);

        NewStepTitle = string.Empty;
        await ReloadStepsAsync();
        _owner.RaiseToast("Step added");
        await _owner.RefreshCountsOnlyAsync();
    }

    private async Task ToggleStepAsync(TodoTaskViewModel? step)
    {
        if (step is null)
            return;

        var requested = step.IsCompleted ? (int)TaskState.Completed : (int)TaskState.Incomplete;
        var priorIsCompleted = !step.IsCompleted;

        TaskStatusResult result;
        try
        {
            result = await TaskRepository.UpdateTaskStatusAsync(
                step.TaskID, requested, _owner.CurrentUserId);
        }
        catch
        {
            step.IsCompleted = priorIsCompleted;
            throw;
        }

        if (result.StatusForced)
        {
            step.IsCompleted = priorIsCompleted;
            _owner.RaiseToast("The step could not be changed as requested.");
            return;
        }

        step.IsCompleted = result.TaskStatusID == (int)TaskState.Completed;

        UpdateStepsSummary();
        _owner.RaiseToast(step.IsCompleted ? "Step completed" : "Step marked incomplete");
        await _owner.RefreshCountsOnlyAsync();
    }

    private async Task RemoveStepAsync(TodoTaskViewModel? step)
    {
        if (step is null)
            return;

        await TaskRepository.DeleteTaskAsync(step.TaskID, _owner.CurrentUserId);
        Steps.Remove(step);
        UpdateStepsSummary();
        _owner.RaiseToast("Step removed");
        await _owner.RefreshCountsOnlyAsync();
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
        _owner.RaiseToast("Task deleted");
        await _owner.RefreshAfterMutationAsync();
    }

    private async Task SaveAsync()
    {
        var assigneeIds = SelectedAssignee is null
            ? Array.Empty<int>()
            : new[] { SelectedAssignee.UserID };

        await TaskRepository.SaveTaskAsync(
            TaskID,
            _model.TaskParentID,
            _model.TaskStatusID,
            _model.UserID,
            _model.GroupID,
            TaskName,
            LevelOfImportanceId,
            Description,
            _model.Color ?? "#FFFFFF",
            DueDate,
            EndDate,
            SelectedRecurrence?.Type.RepetitionTypeID ?? 1,
            assigneeIds,
            _owner.CurrentUserId);

        _owner.RaiseToast("Task saved");
        await _owner.RefreshAfterMutationAsync();
    }
}
