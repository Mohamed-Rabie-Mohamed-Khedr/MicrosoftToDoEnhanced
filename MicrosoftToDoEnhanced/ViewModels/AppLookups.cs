using Core.Repositories;

namespace MicrosoftToDoEnhanced.ViewModels;

/// <summary>
/// Cache of the small lookup tables every task row needs (status/level names, possible
/// assignees, recurrence options). Loaded once and reused so repeated refreshes never
/// re-query the same static data.
/// </summary>
public sealed class AppLookups
{
    public Dictionary<int, string> StatusNames { get; } = new();
    public Dictionary<int, string> LevelNames { get; } = new();
    public List<User> Assignees { get; } = new();
    public List<RecurrenceOption> RecurrenceOptions { get; } = new();

    public async Task ReloadAsync()
    {
        StatusNames.Clear();
        foreach (var status in await TaskRepository.GetTaskStatusesAsync())
            StatusNames[status.TaskStatusID] = status.StatusName;

        LevelNames.Clear();
        foreach (var level in await TaskRepository.GetImportanceLevelsAsync())
            LevelNames[level.LevelOfImportanceID] = level.LevelName;

        Assignees.Clear();
        Assignees.AddRange(await UserRepository.GetAllUsersAsync());

        RecurrenceOptions.Clear();
        foreach (var type in await PlannedTaskRepository.GetRepetitionTypesAsync())
            RecurrenceOptions.Add(new RecurrenceOption(type));
    }
}