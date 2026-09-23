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

public enum PlannedSort
{
    Ranking,
    Date,
    Importance
}

public static class PlannedTaskRepository
{
    public static async Task AddAsync(int taskId, DateTime startDate, DateTime? endDate, int repetitionTypeId)
    {
        await SqlHelper.ExecuteAsync("AddPlanned", true,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@PlannedStartDate", SqlDbType.DateTime2) { Value = startDate },
            new SqlParameter("@PlannedEndDate", SqlDbType.DateTime2)
            {
                Value = endDate ?? (object)DBNull.Value
            },
            new SqlParameter("@RepetitionTypeID", SqlDbType.Int) { Value = repetitionTypeId });
    }

    public static async Task UpdateAsync(
        int plannedId, int taskId, DateTime startDate, DateTime? endDate, int repetitionTypeId)
    {
        await SqlHelper.ExecuteAsync("UpdatePlanned", true,
            new SqlParameter("@PlannedID", SqlDbType.Int) { Value = plannedId },
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@PlannedStartDate", SqlDbType.DateTime2) { Value = startDate },
            new SqlParameter("@PlannedEndDate", SqlDbType.DateTime2)
            {
                Value = endDate ?? (object)DBNull.Value
            },
            new SqlParameter("@RepetitionTypeID", SqlDbType.Int) { Value = repetitionTypeId });
    }

    public static async Task DeleteAsync(int plannedId)
    {
        await SqlHelper.ExecuteAsync("DeletePlanned", true,
            new SqlParameter("@PlannedID", SqlDbType.Int) { Value = plannedId });
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

    public static async Task<List<PlannedTask>> GetPlannedTasksAsync(int userId, PlannedScope scope, PlannedSort sort)
    {
        var procedure = (scope, sort) switch
        {
            (PlannedScope.All, PlannedSort.Date) => "GetTasksPlannedByDate",
            (PlannedScope.All, PlannedSort.Importance) => "GetTasksPlannedByImportance",
            (PlannedScope.All, _) => "GetTasksPlannedByRanking",
            (PlannedScope.Daily, PlannedSort.Importance) => "GetTasksDailyByImportance",
            (PlannedScope.Daily, _) => "GetTasksDailyByRanking",
            (PlannedScope.Weekly, PlannedSort.Importance) => "GetTasksWeeklyByImportance",
            (PlannedScope.Weekly, _) => "GetTasksWeeklyByRanking",
            (PlannedScope.Monthly, PlannedSort.Importance) => "GetTasksMonthlyByImportance",
            (PlannedScope.Monthly, _) => "GetTasksMonthlyByRanking",
            (PlannedScope.Yearly, PlannedSort.Importance) => "GetTasksYearlyByImportance",
            (PlannedScope.Yearly, _) => "GetTasksYearlyByRanking"
        };

        var rows = await SqlHelper.QueryAsync(procedure, true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId });
        return rows.Select(r => new PlannedTask(r)).ToList();
    }

    /// <summary>
    /// Counts visible (non-completed) planned tasks for a smart-view badge,
    /// mirroring the same date/recurrence conditions the corresponding procedures use.
    /// </summary>
    public static async Task<int> CountPlannedTasksAsync(int userId, PlannedScope scope)
    {
        var sql = """
                  SELECT COUNT(*) FROM Planned P
                  JOIN Tasks T ON P.TaskID = T.TaskID
                  WHERE T.UserID = @UserID AND T.TaskParentID IS NULL AND T.TaskStatusID <> 3
                  """;
        switch (scope)
        {
            case PlannedScope.Daily:
                sql += " AND CAST(P.PlannedStartDate AS DATE) BETWEEN CAST(GETDATE() AS DATE) AND CAST(ISNULL(P.PlannedEndDate, GETDATE()) AS DATE)";
                break;
            case PlannedScope.Weekly:
                sql += " AND P.RepetitionTypeID = 3";
                break;
            case PlannedScope.Monthly:
                sql += " AND P.RepetitionTypeID = 4";
                break;
            case PlannedScope.Yearly:
                sql += " AND P.RepetitionTypeID = 5";
                break;
        }

        var result = await SqlHelper.ScalarAsync(sql,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId });
        return Convert.ToInt32(result);
    }
}