using Xunit;

public class TaskSearchTests
{
    private static RoraQuestService Service() => new(ProgressTestStores.Create());

    private static TaskItem Add(
        RoraQuestService service, string title, Guid? category = null, Guid? subcategory = null,
        string owner = "one", DateOnly? week = null, DateOnly? date = null,
        TaskStatus status = TaskStatus.Todo) =>
        service.CreateTask(owner, ProgressTestStores.Task(title, status, week ?? new DateOnly(2026, 10, 5), date)
            with { CategoryId = category, SubCategoryId = subcategory });

    private static Guid[] Search(RoraQuestService service, string? text, string owner = "one") =>
        service.GetTasks(owner, new TaskQuery { Search = text }).Select(task => task.Id).ToArray();

    [Theory]
    [InlineData("cakes", "Bake Cakes")]
    [InlineData("two sum", "Two Sum")]
    [InlineData("three sum", "Three Sum")]
    [InlineData("  TwO SuM \t", "Two Sum")]
    [InlineData("ree s", "Three Sum")]
    public void Names_AreTrimmedCaseInsensitiveLiteralSubstrings(string query, string expected)
    {
        var service = Service();
        var tasks = new[] { "Bake Cakes", "Two Sum", "Three Sum", "2sum", "Unrelated" }
            .Select(title => Add(service, title)).ToArray();

        Assert.Equal(new[] { tasks.Single(task => task.Title == expected).Id }, Search(service, query));
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("*")]
    [InlineData("[a+b]")]
    [InlineData("?")]
    [InlineData("two  sum")]
    [InlineData("蛋糕")]
    public void SpecialCharactersAndInternalSpaces_AreLiteral(string query)
    {
        var service = Service();
        var match = Add(service, $"Practice {query} today");
        Add(service, "two sum");
        Add(service, "ordinary task");
        Assert.Equal(new[] { match.Id }, Search(service, query));
    }

    [Fact]
    public void CategoryAndSubcategoryNames_IncludeDescendantsAndBothAssignmentLayouts()
    {
        var service = Service();
        var root = service.CreateCategory("one", new("DSA", null));
        var child = service.CreateCategory("one", new("Arrays", root.Id));
        var grandchild = service.CreateCategory("one", new("Two Pointers", child.Id));
        var sibling = service.CreateCategory("one", new("Graphs", root.Id));
        var direct = Add(service, "Root task", root.Id);
        var imported = Add(service, "Two Sum", root.Id, child.Id);
        var manual = Add(service, "Three Sum", child.Id);
        var subOnly = Add(service, "Pair search", subcategory: grandchild.Id);
        var nested = Add(service, "Triple search", grandchild.Id);
        var graph = Add(service, "Traversal", root.Id, sibling.Id);
        Add(service, "Unrelated");

        Assert.Equal(new[] { direct.Id, imported.Id, manual.Id, subOnly.Id, nested.Id, graph.Id }, Search(service, "dsa"));
        Assert.Equal(new[] { imported.Id, manual.Id, subOnly.Id, nested.Id }, Search(service, "  ARRays "));
        Assert.Equal(new[] { subOnly.Id, nested.Id }, Search(service, "pointers"));
        Assert.Equal(new[] { graph.Id }, Search(service, "graphs"));
    }

    [Fact]
    public void Results_AreCompleteAcrossWeeksDaysStatuses_AndNotRepeatedForMultipleMatches()
    {
        var service = Service();
        var root = service.CreateCategory("one", new("Cakes", null));
        var child = service.CreateCategory("one", new("Cakes decoration", root.Id));
        var tasks = Enumerable.Range(0, 137).Select(i => Add(service, $"Cakes {i}",
            root.Id, child.Id, week: new DateOnly(2024, 1, 1).AddDays(7 * i),
            date: i % 2 == 0 ? null : new DateOnly(2024, 1, 1).AddDays(7 * i),
            status: Enum.GetValues<TaskStatus>()[i % 5])).ToArray();

        var results = service.GetTasks("one", new TaskQuery { Search = "cakes" });

        Assert.Equal(tasks.Select(task => task.Id), results.Select(task => task.Id));
        Assert.Equal(137, results.Select(task => task.Id).Distinct().Count());
        Assert.Equal(5, results.Select(task => task.Status).Distinct().Count());
        Assert.Contains(results, task => task.PlannedDate is null);
        Assert.True(results.First().PlannedWeekStart.Year < results.Last().PlannedWeekStart.Year);
    }

    [Fact]
    public void RenamesAndDeletedReferences_UseOnlyCurrentNames_AndStillMatchTitles()
    {
        var service = Service();
        var category = service.CreateCategory("one", new("Old Name", null));
        var task = Add(service, "Cakes", category.Id);
        Assert.Equal(new[] { task.Id }, Search(service, "old name"));

        service.UpdateCategory("one", category.Id, new("New Name", null));
        Assert.Empty(Search(service, "old name"));
        Assert.Equal(new[] { task.Id }, Search(service, "new name"));
        service.DeleteCategory("one", category.Id);
        Assert.Empty(Search(service, "new name"));
        Assert.Equal(new[] { task.Id }, Search(service, "cakes"));
        var missing = Add(service, "Two Sum", Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(new[] { missing.Id }, Search(service, "two sum"));
    }

    [Fact]
    public void MalformedCategoryCycles_TerminateAndReturnEachTaskOnce()
    {
        var service = Service();
        var first = service.CreateCategory("one", new("First", null));
        var second = service.CreateCategory("one", new("Second", first.Id));
        service.UpdateCategory("one", first.Id, new("First", second.Id));
        var task = Add(service, "Cycle task", second.Id, first.Id);
        Assert.Equal(new[] { task.Id }, Search(service, "first"));
    }

    [Fact]
    public void OwnerScope_IsPreservedForBothTasksAndCategoryNames()
    {
        var service = Service();
        var foreignCategory = service.CreateCategory("other", new("Private category", null));
        Add(service, "Private task", foreignCategory.Id, owner: "other");
        var own = Add(service, "Own task", foreignCategory.Id);
        var child = service.CreateCategory("one", new("Own category", foreignCategory.Id));
        Add(service, "Own descendant", child.Id);

        Assert.Empty(Search(service, "private"));
        Assert.Equal(new[] { own.Id }, Search(service, "own task"));
        Assert.Single(Search(service, "private", "other"));
        Assert.Empty(Search(service, "own task", "other"));
        Assert.Empty(Search(service, "private", "nobody"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public void BlankSearch_PreservesExistingListAndOrder(string? query)
    {
        var service = Service();
        Add(service, "Unscheduled", week: new(2026, 10, 12));
        var laterDay = Add(service, "Later", date: new(2026, 10, 9));
        var earlierDay = Add(service, "Earlier", date: new(2026, 10, 5));
        var all = service.GetTasks("one", new TaskQuery());

        Assert.Equal(new[] { earlierDay.Id, laterDay.Id }, all.Take(2).Select(t => t.Id));
        Assert.Equal(all.Select(task => task.Id), Search(service, query));
    }

    [Fact]
    public void Search_ComposesWithAllExistingFilters_WithoutChangingTheirMeaning()
    {
        var service = Service();
        var root = service.CreateCategory("one", new("Baking", null));
        var child = service.CreateCategory("one", new("Cakes", root.Id));
        var match = Add(service, "Cake one", root.Id, child.Id, status: TaskStatus.Done);
        // Existing From/To filters use StartAt/EndAt, not planned date.
        match.StartAt = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
        match.EndAt = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
        Add(service, "Cake two", root.Id, child.Id);
        Add(service, "Cake three", root.Id, child.Id, week: new(2026, 10, 12), status: TaskStatus.Done);
        Add(service, "Cake four", child.Id, status: TaskStatus.Done);
        var query = TaskQuery.FromHttp(ProgressTestStores.Query(
            $"?search=cakes&categoryId={root.Id}&subCategoryId={child.Id}&status=Done" +
            "&weekStart=2026-10-05&from=2026-10-05&to=2026-10-05"));

        Assert.Equal(new[] { match.Id }, service.GetTasks("one", query).Select(task => task.Id));
        query.Search = "not found";
        Assert.Empty(service.GetTasks("one", query));
    }

    [Fact]
    public void Search_DoesNotSearchPrivateNotesOrUnassignedCategories()
    {
        var service = Service();
        service.CreateCategory("one", new("Unused category", null));
        var task = Add(service, "Not a match");
        task.Description = "hidden phrase";
        task.LogicNotes = "hidden phrase";
        task.Pattern = "hidden phrase";
        task.AiReviewFeedback = "hidden phrase";
        Assert.Empty(Search(service, "hidden phrase"));
        Assert.Empty(Search(service, "unused category"));
    }

    [Fact]
    public void QueryParsing_DecodesSearchAndUsesFirstRepeatedValueLikeExistingFilters()
    {
        var parsed = TaskQuery.FromHttp(ProgressTestStores.Query("?search=two+sum%2B%25%26&search=ignored"));
        Assert.Equal("two sum+%&", parsed.Search);
        Assert.Null(TaskQuery.FromHttp(ProgressTestStores.Query()).Search);
    }
}
