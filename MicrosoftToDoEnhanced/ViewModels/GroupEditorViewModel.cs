using System.Windows.Input;
using Core.Repositories;
using MicrosoftToDoEnhanced.Services;

namespace MicrosoftToDoEnhanced.ViewModels;

public class GroupEditorViewModel : ViewModelBase
{
    private readonly User _currentUser;
    private readonly int? _groupId;
    private readonly int _adminId;
    private readonly MessageBoxConfirmationService _confirmation;
    private string? _groupName;
    private string? _groupDescription;
    private string _color = "#0078D4";

    public GroupEditorViewModel(
        User currentUser,
        MainViewModel owner,
        Group? group = null,
        MessageBoxConfirmationService? confirmation = null)
    {
        _currentUser = currentUser;
        _groupId = group?.GroupID;
        _confirmation = confirmation ?? new MessageBoxConfirmationService();
        Owner = owner;

        if (group is not null)
        {
            GroupName = group.GroupName;
            GroupDescription = group.GroupDescription;
            Color = group.Color;
            IsOwner = group.AdminID == currentUser.UserID;
            _adminId = group.AdminID;
        }

        SaveGroupCommand = new AsyncRelayCommand(SaveGroupAsync, onError: OnCommandError);
        DeleteGroupCommand = new AsyncRelayCommand(DeleteGroupAsync, canExecute: () => IsEditMode, onError: OnCommandError);
    }

    private MainViewModel Owner { get; }

    public event EventHandler? RequestClose;

    public ICommand SaveGroupCommand { get; }
    public ICommand DeleteGroupCommand { get; }

    public bool IsEditMode => _groupId is not null;
    public bool IsOwner { get; }
    public int? GroupId { get; private set; }
    public bool Deleted { get; private set; }

    public string EditorTitle => IsEditMode ? "Edit group" : "Create group";
    public string PrimaryActionText => IsEditMode ? "Save changes" : "Create";

    public string? GroupName
    {
        get => _groupName;
        set => SetProperty(ref _groupName, value);
    }

    public string? GroupDescription
    {
        get => _groupDescription;
        set => SetProperty(ref _groupDescription, value);
    }

    public string Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }

    public List<string> ColorChoices { get; } =
    [
        "#0078D4", "#5C2D91", "#D83B01", "#107C10", "#C239B3",
        "#008575", "#8764B8", "#498205", "#CA5010", "#E74856",
        "#00B7C3", "#FF8C00", "#767676", "#000000"
    ];

    public Group GetUpdatedGroup() => new()
    {
        GroupID = _groupId!.Value,
        AdminID = _adminId,
        GroupName = GroupName ?? string.Empty,
        GroupDescription = GroupDescription,
        Color = Color
    };

    private async Task SaveGroupAsync()
    {
        var input = GetValidatedInput();
        if (input is null)
            return;

        if (_groupId is { } groupId)
        {
            await GroupRepository.UpdateGroupAsync(
                groupId, _currentUser.UserID, input.Value.Name, input.Value.Description, Color);
            Owner.RaiseToast("Group saved");
        }
        else
        {
            GroupId = await GroupRepository.AddGroupAsync(
                _currentUser.UserID, input.Value.Name, input.Value.Description, Color);
            Owner.RaiseToast("Group created");
        }

        Close();
    }

    private async Task DeleteGroupAsync()
    {
        var confirmed = _confirmation.Confirm(
            "Delete group",
            "This deletes the group, its tasks and its feed for everyone. Continue?");
        if (!confirmed)
            return;

        await GroupRepository.DeleteGroupAsync(_groupId!.Value, _currentUser.UserID);
        Deleted = true;
        Owner.RaiseToast("Group deleted");
        Close();
    }

    private void Close() => RequestClose?.Invoke(this, EventArgs.Empty);

    private void OnCommandError(Exception exception) =>
        Owner.RaiseToast(DescribeError(exception));

    private (string Name, string? Description)? GetValidatedInput()
    {
        if (string.IsNullOrWhiteSpace(GroupName))
        {
            Owner.RaiseToast("Group name is required");
            return null;
        }

        var name = GroupName.Trim();
        var description = string.IsNullOrWhiteSpace(GroupDescription) ? null : GroupDescription.Trim();
        return (name, description);
    }
}
