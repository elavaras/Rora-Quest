using Xunit;

public class TaskAiReviewFeedbackTests
{
    private static RoraQuestService CreateService()
    {
        var state = new AppState();
        var store = new InMemoryRoraQuestStore(state);
        return new RoraQuestService(store);
    }

    private static CreateTaskRequest MinimalCreateRequest(string title = "System Design Task") =>
        new(
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

    [Fact]
    public void CreateTask_WithAiReviewFeedback_PersistsValue()
    {
        var service = CreateService();
        var expected = "AI suggests improving load balancing trade-off explanation.";

        var task = service.CreateTask("user1", MinimalCreateRequest() with
        {
            AiReviewFeedback = expected
        });

        Assert.Equal(expected, task.AiReviewFeedback);
    }

    [Fact]
    public void GetTask_ReturnsAiReviewFeedback()
    {
        var service = CreateService();
        var expected = "Focus more on data consistency and failure handling.";
        var created = service.CreateTask("user1", MinimalCreateRequest() with { AiReviewFeedback = expected });

        var loaded = service.GetTask("user1", created.Id);

        Assert.NotNull(loaded);
        Assert.Equal(expected, loaded!.AiReviewFeedback);
    }

    [Fact]
    public void UpdateTask_SetsAiReviewFeedback()
    {
        var service = CreateService();
        var created = service.CreateTask("user1", MinimalCreateRequest());
        var expected = "AI feedback: add cache invalidation strategy and estimate hotspot read traffic.";

        var result = service.UpdateTask(
            "user1",
            created.Id,
            new UpdateTaskRequest(
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
                AiReviewFeedback: expected));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(expected, result.Value!.AiReviewFeedback);
    }
}
