using Xunit;

public class TaskWorkflowPolicyTests
{
    private static RoraQuestService CreateService()
    {
        var state = new AppState();
        var store = new InMemoryRoraQuestStore(state);
        return new RoraQuestService(store);
    }

    private static CreateTaskRequest MinimalCreateRequest(string title = "Task", Guid? categoryId = null) =>
        new(
            Title: title,
            Description: null,
            CategoryId: categoryId,
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
    public void CreateTask_ForDsaCategory_ThrowsInvalidOperation()
    {
        var svc = CreateService();
        var dsa = svc.CreateCategory("user1", new CreateCategoryRequest("DSA", null));

        Assert.Throws<InvalidOperationException>(() => svc.CreateTask("user1", MinimalCreateRequest(categoryId: dsa.Id)));
    }

    [Fact]
    public void UpdateTask_CategoryMutation_IsBlocked()
    {
        var svc = CreateService();
        var otherCategory = svc.CreateCategory("user1", new CreateCategoryRequest("Arrays", null));
        var task = svc.CreateTask("user1", MinimalCreateRequest());

        var result = svc.UpdateTask(
            "user1",
            task.Id,
            new UpdateTaskRequest(
                Title: null,
                Description: null,
                CategoryId: otherCategory.Id,
                SubCategoryId: null,
                PlannedWeekStart: null,
                PlannedDate: null,
                DueDate: null,
                StartAt: null,
                EndAt: null,
                Priority: null,
                IfMatchVersion: null));

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("category updates", result.Error);
    }

    [Fact]
    public void DsaTask_ManualStatusAndSubtaskStructureUpdates_AreBlocked_ButCompletionToggleIsAllowed()
    {
        var svc = CreateService();
        var import = svc.CreateChecklistImport(
            "user1",
            new BulkChecklistImportRequest("Week 1: Arrays\nTwo Sum", "DSA", null));
        _ = svc.CommitChecklistImport("user1", import.Id, null, null);
        var dsaTask = Assert.Single(svc.GetTasks("user1", new TaskQuery()));
        Assert.NotEmpty(dsaTask.SubSteps);

        var statusResult = svc.UpdateTaskStatus(
            "user1",
            dsaTask.Id,
            new UpdateTaskStatusRequest(TaskStatus.InProgress, OverrideIncompleteSubsteps: false, IfMatchVersion: null));
        var createSubstepResult = svc.CreateSubstep("user1", dsaTask.Id, new CreateSubstepRequest("Extra step", 5));
        var updateSubstepResult = svc.UpdateSubstep(
            "user1",
            dsaTask.Id,
            dsaTask.SubSteps[0].Id,
            new UpdateSubstepRequest(Title: null, IsDone: true, IfMatchVersion: null));
        var renameSubstepResult = svc.UpdateSubstep(
            "user1",
            dsaTask.Id,
            dsaTask.SubSteps[0].Id,
            new UpdateSubstepRequest(Title: "Renamed step", IsDone: null, IfMatchVersion: null));

        Assert.Equal(400, statusResult.StatusCode);
        Assert.Equal(400, createSubstepResult.StatusCode);
        Assert.Equal(200, updateSubstepResult.StatusCode);
        Assert.True(updateSubstepResult.Value!.IsDone);
        Assert.Equal(400, renameSubstepResult.StatusCode);
    }

    [Fact]
    public void NonDsaTask_ManualStatusAndSubtaskUpdates_AreAllowed()
    {
        var svc = CreateService();
        var task = svc.CreateTask("user1", MinimalCreateRequest());
        Assert.Empty(task.SubSteps);

        var statusResult = svc.UpdateTaskStatus(
            "user1",
            task.Id,
            new UpdateTaskStatusRequest(TaskStatus.InProgress, OverrideIncompleteSubsteps: false, IfMatchVersion: null));
        var createSubstepResult = svc.CreateSubstep("user1", task.Id, new CreateSubstepRequest("Extra step", 5));
        var createdSubstep = Assert.IsType<TaskSubStep>(createSubstepResult.Value);
        var updateSubstepResult = svc.UpdateSubstep(
            "user1",
            task.Id,
            createdSubstep.Id,
            new UpdateSubstepRequest(Title: null, IsDone: true, IfMatchVersion: null));

        Assert.Equal(200, statusResult.StatusCode);
        Assert.Equal(200, createSubstepResult.StatusCode);
        Assert.Equal(200, updateSubstepResult.StatusCode);
        Assert.True(updateSubstepResult.Value!.IsDone);
    }

    [Fact]
    public void NonDsaTask_DoesNotSeedDefaultSubtasks_ButAllowsManualCreation()
    {
        var svc = CreateService();
        var task = svc.CreateTask("user1", MinimalCreateRequest());

        Assert.Empty(task.SubSteps);

        var createSubstepResult = svc.CreateSubstep("user1", task.Id, new CreateSubstepRequest("Manual step", 3));

        Assert.Equal(200, createSubstepResult.StatusCode);
        var createdSubstep = Assert.IsType<TaskSubStep>(createSubstepResult.Value);
        Assert.Equal("Manual step", createdSubstep.Title);
        Assert.Equal(3, createdSubstep.Weight);
    }
}
