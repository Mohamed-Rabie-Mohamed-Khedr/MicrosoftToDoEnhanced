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

    /// <summary>
    /// Fetches the most recent Planned record for each taskId in a single query.
    /// Equivalent to calling GetPlannedAsync in a loop, but without N round-trips.
    /// </summary>
    public static async Task<IReadOnlyDictionary<int, PlannedTask>> GetPlannedForTasksAsync(
        IReadOnlyList<int> taskIds)
    {
        var dict = new Dictionary<int, PlannedTask>(taskIds.Count);
        if (taskIds.Count == 0)
            return dict;

        var tvp = SqlHelper.BuildTaskIdTable(taskIds);
        var rows = await SqlHelper.QueryAsync(
            @"SELECT p.*
              FROM Planned p
              INNER JOIN @TaskIDs tvp ON p.TaskID = tvp.TaskID
              WHERE NOT EXISTS (
                  SELECT 1
                  FROM Planned p2
                  WHERE p2.TaskID = p.TaskID
                    AND p2.PlannedStartDate > p.PlannedStartDate
              )", false,
            SqlHelper.Tvp("@TaskIDs", "TVPTaskIDs", tvp));

        foreach (DataRow row in rows)
        {
            var planned = new PlannedTask(row);
            dict[planned.TaskID] = planned;
        }

        return dict;
    }

    public static async Task<List<TodoTask>> GetTasksAsync(int userId, PlannedScope scope)
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
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId });
        return rows.Select(r => new TodoTask(r)).ToList();
    }
}
