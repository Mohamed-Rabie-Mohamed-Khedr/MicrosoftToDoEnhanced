using System.Data;
using Core.Data;
using Microsoft.Data.SqlClient;

namespace Core.Repositories;

public enum PlannedScope
{
    All,
    Daily,
    Weekly,
    Monthly,
    Yearly
}

public static class PlannedTaskRepository
{
    public static async Task<int> AddAsync(
        int taskId, DateTime startDate, DateTime? endDate, int repetitionTypeId, int actingUserId)
    {
        var plannedId = await SqlHelper.ExecuteProcReturnAsync("AddPlanned",
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@PlannedStartDate", SqlDbType.DateTime2) { Value = startDate },
            new SqlParameter("@PlannedEndDate", SqlDbType.DateTime2)
            {
                Value = endDate ?? (object)DBNull.Value
            },
            new SqlParameter("@RepetitionTypeID", SqlDbType.Int) { Value = repetitionTypeId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

        return plannedId ?? throw new InvalidOperationException("The plan could not be saved.");
    }

    public static async Task UpdateAsync(
        int plannedId, int taskId, DateTime startDate, DateTime? endDate, int repetitionTypeId, int actingUserId)
    {
        await SqlHelper.ExecuteAsync("UpdatePlanned", true,
            new SqlParameter("@PlannedID", SqlDbType.Int) { Value = plannedId },
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@PlannedStartDate", SqlDbType.DateTime2) { Value = startDate },
            new SqlParameter("@PlannedEndDate", SqlDbType.DateTime2)
            {
                Value = endDate ?? (object)DBNull.Value
            },
            new SqlParameter("@RepetitionTypeID", SqlDbType.Int) { Value = repetitionTypeId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
    }

    public static async Task DeleteAsync(int plannedId, int actingUserId)
    {
        await SqlHelper.ExecuteAsync("DeletePlanned", true,
            new SqlParameter("@PlannedID", SqlDbType.Int) { Value = plannedId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
    }

    public static async Task AutoUpdateAsync()
    {
        await SqlHelper.ExecuteAsync("AutoUpdatePlanneds", true);
    }

    public static async Task<PlannedTask?> GetPlannedAsync(int taskId)
    {
        var rows = await SqlHelper.QueryAsync(
            "SELECT TOP 1 * FROM Planned WHERE TaskID = @TaskID ORDER BY PlannedStartDate DESC", false,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId });
        return rows.Count == 0 ? null : new PlannedTask(rows[0]);
    }

    public static async Task<List<RepetitionType>> GetRepetitionTypesAsync()
    {
        var rows = await SqlHelper.QueryAsync(
            "SELECT * FROM RepetitionTypes ORDER BY RepetitionTypeID", false);
        return rows.Select(r => new RepetitionType(r)).ToList();
    }

    public static async Task<List<TodoTask>> GetTasksAsync(int userId, PlannedScope scope, int actingUserId)
    {
        var procedure = scope switch
        {
            PlannedScope.Daily => "GetTasksToday",
            PlannedScope.Weekly => "GetTasksWeekly",
            PlannedScope.Monthly => "GetTasksMonthly",
            PlannedScope.Yearly => "GetTasksYearly",
            _ => "GetTasksPlannedByDate"
        };

        var rows = await SqlHelper.QueryAsync(procedure, true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
        return rows.Select(r => new TodoTask(r)).ToList();
    }
}
