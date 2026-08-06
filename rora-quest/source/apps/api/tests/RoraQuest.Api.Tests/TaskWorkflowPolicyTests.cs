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

    private static (TaskItem Task, TaskSubStep First, TaskSubStep Second) CreateTaskWithTwoSubsteps(
        RoraQuestService svc,
        TaskStatus status = TaskStatus.Todo)
    {
        var task = svc.CreateTask("user1", MinimalCreateRequest());
        var first = Assert.IsType<TaskSubStep>(
            svc.CreateSubstep("user1", task.Id, new CreateSubstepRequest("First", 1)).Value);
        var second = Assert.IsType<TaskSubStep>(
            svc.CreateSubstep("user1", task.Id, new CreateSubstepRequest("Second", 1)).Value);
        if (status != TaskStatus.Todo)
        {
            Assert.Equal(
                200,
                svc.UpdateTaskStatus(
                    "user1",
                    task.Id,
                    new UpdateTaskStatusRequest(status, OverrideIncompleteSubsteps: true, IfMatchVersion: null)).StatusCode);
        }

        return (task, first, second);
    }

    [Fact]
    public void CreateTask_ForDsaCategory_SeedsStandardSubSteps()
    {
        var svc = CreateService();
        var dsa = svc.CreateCategory("user1", new CreateCategoryRequest("DSA", null));

        var task = svc.CreateTask("user1", MinimalCreateRequest(categoryId: dsa.Id));

        Assert.Equal(dsa.Id, task.CategoryId);
        Assert.Equal(RoraQuestService.StandardSubStepTemplate.Length, task.SubSteps.Count);
        Assert.All(task.SubSteps, step => Assert.False(step.IsDone));
        Assert.Equal(
            RoraQuestService.StandardSubStepTemplate.Select(template => template.Title),
            task.SubSteps.Select(step => step.Title));
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
    public void DsaTask_ManualCreation_StatusAndSubtaskStructureUpdates_AreBlocked_ButCompletionToggleIsAllowed()
    {
        var svc = CreateService();
        var dsa = svc.CreateCategory("user1", new CreateCategoryRequest("DSA", null));
        var dsaTask = svc.CreateTask("user1", MinimalCreateRequest(categoryId: dsa.Id));
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

    [Fact]
    public void UpdateSubstep_NonFinalCompletion_DoesNotPromoteParent()
    {
        var svc = CreateService();
        var (task, first, _) = CreateTaskWithTwoSubsteps(svc);
        var taskVersion = task.RowVersion;
        var substepVersion = first.RowVersion;
        var eventCount = task.StatusEvents.Count;

        var result = svc.UpdateSubstep(
            "user1",
            task.Id,
            first.Id,
            new UpdateSubstepRequest(Title: null, IsDone: true, IfMatchVersion: substepVersion));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(TaskStatus.Todo, task.Status);
        Assert.Equal(eventCount, task.StatusEvents.Count);
        Assert.Equal(taskVersion + 1, task.RowVersion);
        Assert.Equal(substepVersion + 1, first.RowVersion);
        Assert.Equal(first.CompletedAt, task.UpdatedAt);
    }

    [Theory]
    [InlineData(TaskStatus.Todo)]
    [InlineData(TaskStatus.InProgress)]
    public void UpdateSubstep_FinalCompletion_PromotesActiveParentWithSingleEvent(TaskStatus initialStatus)
    {
        var svc = CreateService();
        var (task, first, second) = CreateTaskWithTwoSubsteps(svc, initialStatus);
        _ = svc.UpdateSubstep(
            "user1",
            task.Id,
            first.Id,
            new UpdateSubstepRequest(Title: null, IsDone: true, IfMatchVersion: first.RowVersion));
        var taskVersion = task.RowVersion;
        var substepVersion = second.RowVersion;
        var eventCount = task.StatusEvents.Count;

        var result = svc.UpdateSubstep(
            "user1",
            task.Id,
            second.Id,
            new UpdateSubstepRequest(Title: null, IsDone: true, IfMatchVersion: substepVersion));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(TaskStatus.Done, task.Status);
        Assert.Equal(taskVersion + 1, task.RowVersion);
        Assert.Equal(substepVersion + 1, second.RowVersion);
        var statusEvent = Assert.Single(task.StatusEvents.Skip(eventCount));
        Assert.Equal(initialStatus, statusEvent.FromStatus);
        Assert.Equal(TaskStatus.Done, statusEvent.ToStatus);
        Assert.Equal(second.CompletedAt, statusEvent.ChangedAt);
        Assert.Equal(second.CompletedAt, task.UpdatedAt);
    }

    [Fact]
    public void UpdateSubstep_UncheckingDoneSubstep_ReopensParentToInProgress()
    {
        var svc = CreateService();
        var (task, first, second) = CreateTaskWithTwoSubsteps(svc);
        _ = svc.UpdateSubstep("user1", task.Id, first.Id, new(null, true, first.RowVersion));
        _ = svc.UpdateSubstep("user1", task.Id, second.Id, new(null, true, second.RowVersion));
        var taskVersion = task.RowVersion;
        var substepVersion = first.RowVersion;
        var eventCount = task.StatusEvents.Count;

        var result = svc.UpdateSubstep(
            "user1",
            task.Id,
            first.Id,
            new UpdateSubstepRequest(Title: null, IsDone: false, IfMatchVersion: substepVersion));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(TaskStatus.InProgress, task.Status);
        Assert.Null(first.CompletedAt);
        Assert.Equal(taskVersion + 1, task.RowVersion);
        Assert.Equal(substepVersion + 1, first.RowVersion);
        var statusEvent = Assert.Single(task.StatusEvents.Skip(eventCount));
        Assert.Equal(TaskStatus.Done, statusEvent.FromStatus);
        Assert.Equal(TaskStatus.InProgress, statusEvent.ToStatus);
        Assert.Equal(task.UpdatedAt, statusEvent.ChangedAt);
    }

    [Fact]
    public void UpdateSubstep_DsaFinalCompletion_PromotesParentAndKeepsManualRestrictions()
    {
        var svc = CreateService();
        var import = svc.CreateChecklistImport(
            "user1",
            new BulkChecklistImportRequest("Week 1: Arrays\nTwo Sum", "DSA", null));
        _ = svc.CommitChecklistImport("user1", import.Id, null, null);
        var task = Assert.Single(svc.GetTasks("user1", new TaskQuery()));

        foreach (var substep in task.SubSteps)
        {
            Assert.Equal(
                200,
                svc.UpdateSubstep(
                    "user1",
                    task.Id,
                    substep.Id,
                    new UpdateSubstepRequest(null, true, substep.RowVersion)).StatusCode);
        }

        Assert.Equal(TaskStatus.Done, task.Status);
        Assert.Equal(
            400,
            svc.UpdateTaskStatus(
                "user1",
                task.Id,
                new UpdateTaskStatusRequest(TaskStatus.InProgress, false, task.RowVersion)).StatusCode);
        Assert.Equal(400, svc.CreateSubstep("user1", task.Id, new CreateSubstepRequest("Extra")).StatusCode);
        Assert.Equal(
            400,
            svc.UpdateSubstep(
                "user1",
                task.Id,
                task.SubSteps[0].Id,
                new UpdateSubstepRequest("Renamed", null, task.SubSteps[0].RowVersion)).StatusCode);
    }

    [Theory]
    [InlineData(TaskStatus.Cancelled)]
    [InlineData(TaskStatus.Skipped)]
    public void UpdateSubstep_FinalCompletion_PreservesTerminalParentStatus(TaskStatus terminalStatus)
    {
        var svc = CreateService();
        var (task, first, second) = CreateTaskWithTwoSubsteps(svc, terminalStatus);
        _ = svc.UpdateSubstep("user1", task.Id, first.Id, new(null, true, first.RowVersion));
        var eventCount = task.StatusEvents.Count;

        var result = svc.UpdateSubstep(
            "user1",
            task.Id,
            second.Id,
            new UpdateSubstepRequest(null, true, second.RowVersion));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(terminalStatus, task.Status);
        Assert.Equal(eventCount, task.StatusEvents.Count);
    }

    [Fact]
    public void UpdateSubstep_SameValues_AreIdempotent()
    {
        var svc = CreateService();
        var (task, first, _) = CreateTaskWithTwoSubsteps(svc);
        var updatedAt = task.UpdatedAt;
        var taskVersion = task.RowVersion;
        var substepVersion = first.RowVersion;
        var eventCount = task.StatusEvents.Count;

        var result = svc.UpdateSubstep(
            "user1",
            task.Id,
            first.Id,
            new UpdateSubstepRequest(first.Title, first.IsDone, first.RowVersion));

        Assert.Equal(200, result.StatusCode);
        Assert.Same(first, result.Value);
        Assert.Equal(updatedAt, task.UpdatedAt);
        Assert.Equal(taskVersion, task.RowVersion);
        Assert.Equal(substepVersion, first.RowVersion);
        Assert.Equal(eventCount, task.StatusEvents.Count);
    }

    [Fact]
    public void UpdateSubstep_RowVersionConflict_DoesNotMutate()
    {
        var svc = CreateService();
        var (task, first, _) = CreateTaskWithTwoSubsteps(svc);
        var updatedAt = task.UpdatedAt;
        var taskVersion = task.RowVersion;
        var substepVersion = first.RowVersion;
        var eventCount = task.StatusEvents.Count;

        var result = svc.UpdateSubstep(
            "user1",
            task.Id,
            first.Id,
            new UpdateSubstepRequest("Changed", true, first.RowVersion + 1));

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("First", first.Title);
        Assert.False(first.IsDone);
        Assert.Null(first.CompletedAt);
        Assert.Equal(updatedAt, task.UpdatedAt);
        Assert.Equal(taskVersion, task.RowVersion);
        Assert.Equal(substepVersion, first.RowVersion);
        Assert.Equal(eventCount, task.StatusEvents.Count);
    }
}
