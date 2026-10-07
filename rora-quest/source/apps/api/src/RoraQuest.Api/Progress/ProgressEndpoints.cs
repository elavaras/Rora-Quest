namespace RoraQuest.Api.Progress;

public static class ProgressEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/progress", (HttpContext http, RoraQuestService service, ILoggerFactory logger) =>
            Respond(http, logger, () => service.GetActivityProgress(UserScope.GetUserId(http), http.Request.Query)));
        api.MapGet("/progress/events", (HttpContext http, RoraQuestService service, ILoggerFactory logger) =>
            Respond(http, logger, () => service.GetActivityProgressEvents(UserScope.GetUserId(http), http.Request.Query)));
    }
    private static IResult Respond<T>(HttpContext http, ILoggerFactory logger, Func<T> action)
    {
        http.Response.Headers.CacheControl = "no-store";
        try { return Results.Ok(action()); }
        catch (ProgressQueryException ex) { return Results.Json(ex.Error, statusCode: 400); }
        catch (Exception ex)
        {
            logger.CreateLogger("Progress").LogError(ex, "Progress snapshot unavailable");
            return Results.Json(new ProgressError("progressUnavailable", "Progress records are unavailable; retry later.", new()), statusCode: 503);
        }
    }
}
