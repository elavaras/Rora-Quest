using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static ProgressHttpTests;

public class TaskSearchHttpTests
{
    [Fact]
    public async Task ExistingTaskRoute_SearchesNamesAndHierarchyAcrossWeeks_WithUnchangedResponseShape()
    {
        await using var factory = new ProgressHttpFactory();
        using var client = factory.Client();
        var root = await Json(await client.PostAsJsonAsync("/api/categories", new { name = "DSA" }));
        var rootId = root.GetProperty("id").GetString();
        var child = await Json(await client.PostAsJsonAsync("/api/categories", new { name = "Arrays", parentCategoryId = rootId }));
        var childId = child.GetProperty("id").GetString();
        var two = await Json(await client.PostAsJsonAsync("/api/tasks",
            new { title = "Two Sum", categoryId = rootId, subCategoryId = childId, plannedWeekStart = "2025-01-06" }));
        var three = await Json(await client.PostAsJsonAsync("/api/tasks",
            new { title = "Three Sum", categoryId = childId, plannedWeekStart = "2027-01-04" }));
        var cakes = await CreateTask(client, "Cakes + tea & icing");

        foreach (var search in new[] { "dsa", " ARRays " })
        {
            var results = await Json(await client.GetAsync("/api/tasks?search=" + Uri.EscapeDataString(search)));
            Assert.Equal(JsonValueKind.Array, results.ValueKind);
            Assert.Equal(new[] { two.GetProperty("id").GetString(), three.GetProperty("id").GetString() },
                results.EnumerateArray().Select(t => t.GetProperty("id").GetString()));
            Assert.All(results.EnumerateArray(), task =>
            {
                Assert.Equal("Todo", task.GetProperty("status").GetString());
                Assert.True(task.TryGetProperty("subSteps", out _));
                Assert.True(task.TryGetProperty("plannedWeekStart", out _));
            });
        }
        foreach (var (search, expected) in new[]
        {
            ("two sum", two.GetProperty("id").GetString()),
            ("three sum", three.GetProperty("id").GetString()),
            ("cakes + tea &", cakes)
        })
        {
            var result = await Json(await client.GetAsync("/api/tasks?search=" + Uri.EscapeDataString(search)));
            Assert.Equal(expected, Assert.Single(result.EnumerateArray()).GetProperty("id").GetString());
        }
        var scoped = await Json(await client.GetAsync("/api/tasks?search=dsa&weekStart=2027-01-04"));
        Assert.Equal(three.GetProperty("id").GetString(), Assert.Single(scoped.EnumerateArray()).GetProperty("id").GetString());
        var detail = await client.GetAsync("/api/tasks/" + cakes);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Fact]
    public async Task ActualRoute_PreservesOwnerIsolationAndBlankBehavior()
    {
        await using var factory = new ProgressHttpFactory();
        using var one = factory.Client();
        using var other = factory.Client("other");
        var ownId = await CreateTask(one, "Cakes");
        var otherId = await CreateTask(other, "Private Cakes");
        foreach (var path in new[] { "/api/tasks", "/api/tasks?search=", "/api/tasks?search=%20%09", "/api/tasks?search=cakes" })
        {
            var own = await Json(await one.GetAsync(path));
            Assert.Equal(ownId, Assert.Single(own.EnumerateArray()).GetProperty("id").GetString());
            var foreign = await Json(await other.GetAsync(path));
            Assert.Equal(otherId, Assert.Single(foreign.EnumerateArray()).GetProperty("id").GetString());
        }
        var noMatches = await Json(await one.GetAsync("/api/tasks?search=private&owner=other"));
        Assert.Empty(noMatches.EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await one.GetAsync("/api/tasks/" + otherId)).StatusCode);
    }
}
