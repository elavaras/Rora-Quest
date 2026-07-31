using Xunit;

/// <summary>
/// Unit tests for the Task Effort Tracking feature.
/// These tests exercise <see cref="RoraQuestService"/> directly using the
/// in-process <see cref="InMemoryRoraQuestStore"/> so no database is required.
/// </summary>
public class TaskEffortTrackingTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static RoraQuestService CreateService()
    {
        var state = new AppState();
        var store = new InMemoryRoraQuestStore(state);
        return new RoraQuestService(store);
    }

    private static CreateTaskRequest MinimalCreateRequest(string title = "Test Task") =>
        new CreateTaskRequest(
            Title: title,
            Description: null,
            CategoryId: null,
            SubCategoryId: null,
            PlannedWeekStart: null,
            PlannedDate: null,
            DueDate: null,
            StartAt: null,
            EndAt: null,
            Priority: null,
            Status: null,
            AssignedTo: null);

    // -----------------------------------------------------------------------
    // CreateTask tests
    // -----------------------------------------------------------------------

    [Fact]
    public void CreateTask_WithEffortFields_SetsAllThree()
    {
        var svc = CreateService();
        var req = MinimalCreateRequest() with
        {
            EstimatedHours = 4.5m,
            ActualHours = 3.0m,
            StoryPoints = 5
        };

        var task = svc.CreateTask("user1", req);

        Assert.Equal(4.5m, task.EstimatedHours);
        Assert.Equal(3.0m, task.ActualHours);
        Assert.Equal(5, task.StoryPoints);
    }

    [Fact]
    public void CreateTask_WithoutEffortFields_LeavesAllNull()
    {
        var svc = CreateService();

        var task = svc.CreateTask("user1", MinimalCreateRequest());

        Assert.Null(task.EstimatedHours);
        Assert.Null(task.ActualHours);
        Assert.Null(task.StoryPoints);
    }

    // -----------------------------------------------------------------------
    // UpdateTask tests
    // -----------------------------------------------------------------------

    [Fact]
    public void UpdateTask_SetsEstimatedHours()
    {
        var svc = CreateService();
        var created = svc.CreateTask("user1", MinimalCreateRequest());

        var req = new UpdateTaskRequest(
            Title: null,
            Description: null,
            CategoryId: null,
            SubCategoryId: null,
            PlannedWeekStart: null,
            PlannedDate: null,
            DueDate: null,
            StartAt: null,
            EndAt: null,
            Priority: null,
            IfMatchVersion: null,
            EstimatedHours: 8.0m);

        var result = svc.UpdateTask("user1", created.Id, req);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(8.0m, result.Value!.EstimatedHours);
        // fields not touched remain null
        Assert.Null(result.Value.ActualHours);
        Assert.Null(result.Value.StoryPoints);
    }

    [Fact]
    public void UpdateTask_SetsActualHours()
    {
        var svc = CreateService();
        var created = svc.CreateTask("user1", MinimalCreateRequest());

        var req = new UpdateTaskRequest(
            Title: null,
            Description: null,
            CategoryId: null,
            SubCategoryId: null,
            PlannedWeekStart: null,
            PlannedDate: null,
            DueDate: null,
            StartAt: null,
            EndAt: null,
            Priority: null,
            IfMatchVersion: null,
            ActualHours: 2.5m);

        var result = svc.UpdateTask("user1", created.Id, req);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(2.5m, result.Value!.ActualHours);
        Assert.Null(result.Value.EstimatedHours);
        Assert.Null(result.Value.StoryPoints);
    }

    [Fact]
    public void UpdateTask_SetsStoryPoints()
    {
        var svc = CreateService();
        var created = svc.CreateTask("user1", MinimalCreateRequest());

        var req = new UpdateTaskRequest(
            Title: null,
            Description: null,
            CategoryId: null,
            SubCategoryId: null,
            PlannedWeekStart: null,
            PlannedDate: null,
            DueDate: null,
            StartAt: null,
            EndAt: null,
            Priority: null,
            IfMatchVersion: null,
            StoryPoints: 13);

        var result = svc.UpdateTask("user1", created.Id, req);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(13, result.Value!.StoryPoints);
        Assert.Null(result.Value.EstimatedHours);
        Assert.Null(result.Value.ActualHours);
    }

    // -----------------------------------------------------------------------
    // AC-5: zero is a valid effort value
    // -----------------------------------------------------------------------

    [Fact]
    public void UpdateTask_ZeroEstimatedHours_IsAccepted()
    {
        var svc = CreateService();
        var created = svc.CreateTask("user1", MinimalCreateRequest());

        var req = new UpdateTaskRequest(
            Title: null,
            Description: null,
            CategoryId: null,
            SubCategoryId: null,
            PlannedWeekStart: null,
            PlannedDate: null,
            DueDate: null,
            StartAt: null,
            EndAt: null,
            Priority: null,
            IfMatchVersion: null,
            EstimatedHours: 0m);

        var result = svc.UpdateTask("user1", created.Id, req);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(0m, result.Value!.EstimatedHours);
    }

    // -----------------------------------------------------------------------
    // AC-7: negative effort values must be rejected
    // -----------------------------------------------------------------------

    [Fact]
    public void UpdateTask_NegativeEstimatedHours_ReturnsValidationError()
    {
        var svc = CreateService();
        var created = svc.CreateTask("user1", MinimalCreateRequest());

        var req = new UpdateTaskRequest(
            Title: null,
            Description: null,
            CategoryId: null,
            SubCategoryId: null,
            PlannedWeekStart: null,
            PlannedDate: null,
            DueDate: null,
            StartAt: null,
            EndAt: null,
            Priority: null,
            IfMatchVersion: null,
            EstimatedHours: -1m);

        var result = svc.UpdateTask("user1", created.Id, req);

        Assert.Equal(400, result.StatusCode);
        Assert.NotNull(result.Error);
        Assert.Contains("EstimatedHours", result.Error);
    }

    [Fact]
    public void UpdateTask_NegativeActualHours_ReturnsValidationError()
    {
        var svc = CreateService();
        var created = svc.CreateTask("user1", MinimalCreateRequest());

        var req = new UpdateTaskRequest(
            Title: null,
            Description: null,
            CategoryId: null,
            SubCategoryId: null,
            PlannedWeekStart: null,
            PlannedDate: null,
            DueDate: null,
            StartAt: null,
            EndAt: null,
            Priority: null,
            IfMatchVersion: null,
            ActualHours: -0.5m);

        var result = svc.UpdateTask("user1", created.Id, req);

        Assert.Equal(400, result.StatusCode);
        Assert.NotNull(result.Error);
        Assert.Contains("ActualHours", result.Error);
    }

    [Fact]
    public void UpdateTask_NegativeStoryPoints_ReturnsValidationError()
    {
        var svc = CreateService();
        var created = svc.CreateTask("user1", MinimalCreateRequest());

        var req = new UpdateTaskRequest(
            Title: null,
            Description: null,
            CategoryId: null,
            SubCategoryId: null,
            PlannedWeekStart: null,
            PlannedDate: null,
            DueDate: null,
            StartAt: null,
            EndAt: null,
            Priority: null,
            IfMatchVersion: null,
            StoryPoints: -3);

        var result = svc.UpdateTask("user1", created.Id, req);

        Assert.Equal(400, result.StatusCode);
        Assert.NotNull(result.Error);
        Assert.Contains("StoryPoints", result.Error);
    }

    [Fact]
    public void UpdateTask_NullEffortFields_LeavesExistingValuesUnchanged()
    {
        // Arrange: create a task with effort values already set
        var svc = CreateService();
        var created = svc.CreateTask("user1", MinimalCreateRequest() with
        {
            EstimatedHours = 6.0m,
            ActualHours = 4.0m,
            StoryPoints = 8
        });

        // Act: send an update with null effort fields (= "no change")
        var req = new UpdateTaskRequest(
            Title: "Updated Title",
            Description: null,
            CategoryId: null,
            SubCategoryId: null,
            PlannedWeekStart: null,
            PlannedDate: null,
            DueDate: null,
            StartAt: null,
            EndAt: null,
            Priority: null,
            IfMatchVersion: null);

        var result = svc.UpdateTask("user1", created.Id, req);

        // Effort values must be unchanged
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(6.0m, result.Value!.EstimatedHours);
        Assert.Equal(4.0m, result.Value.ActualHours);
        Assert.Equal(8, result.Value.StoryPoints);
        Assert.Equal("Updated Title", result.Value.Title);
    }
}
