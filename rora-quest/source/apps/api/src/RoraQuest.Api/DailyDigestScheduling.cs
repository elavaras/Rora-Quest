using System.Net.Http.Json;

public interface ITeamsDigestSender
{
    Task<bool> SendAsync(DailyDigestPayload payload, CancellationToken ct);
}

public sealed class TeamsDigestSender(HttpClient httpClient, IConfiguration config, ILogger<TeamsDigestSender> logger) : ITeamsDigestSender
{
    public async Task<bool> SendAsync(DailyDigestPayload payload, CancellationToken ct)
    {
        var webhookUrl = config["Notifications:Teams:WebhookUrl"];
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            logger.LogWarning("Teams webhook is not configured; cannot deliver digest for user {UserId}.", payload.UserId);
            return false;
        }

        using var response = await httpClient.PostAsJsonAsync(webhookUrl, new { text = payload.Message }, ct);
        if (response.IsSuccessStatusCode) return true;

        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogError("Teams digest delivery failed for user {UserId}. Status: {StatusCode}, Body: {Body}",
            payload.UserId, (int)response.StatusCode, body);
        return false;
    }
}

public sealed class DailyDigestDispatcher(
    RoraQuestService service,
    ITeamsDigestSender sender,
    IConfiguration config,
    ILogger<DailyDigestDispatcher> logger)
{
    public async Task<DailyDigestSendResult> SendForUserAsync(string userId, DateOnly? onDate, bool force, CancellationToken ct)
    {
        var zone = ResolveTimeZone();
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        var targetDate = onDate ?? DateOnly.FromDateTime(nowLocal.DateTime);

        if (!force)
        {
            var settings = service.GetNotificationSettings(userId);
            var nowTime = TimeOnly.FromDateTime(nowLocal.DateTime);
            if (nowTime.Hour != settings.DailyDigestTime.Hour || nowTime.Minute != settings.DailyDigestTime.Minute)
            {
                return new DailyDigestSendResult(userId, targetDate, false, "NotDueYet", settings.TeamsDestination, 0);
            }

            if (service.HasDailyDigestAttemptForDate(userId, targetDate, zone))
            {
                return new DailyDigestSendResult(userId, targetDate, false, "AlreadyAttempted", settings.TeamsDestination, 0);
            }
        }

        var payload = service.GetDailyDigestPayload(userId, targetDate);
        if (!service.IsTeamsConnected(userId))
        {
            service.RecordDailyDigestAttempt(userId, "SkippedNotConnected", null);
            return new DailyDigestSendResult(userId, targetDate, false, "SkippedNotConnected", payload.TeamsDestination, payload.PlannedTaskCount);
        }

        var sent = await sender.SendAsync(payload, ct);
        var status = sent ? "Sent" : "FailedDelivery";
        service.RecordDailyDigestAttempt(userId, status, sent ? DateTimeOffset.UtcNow : null);
        logger.LogInformation("Daily digest dispatch for user {UserId}: {Status}", userId, status);
        return new DailyDigestSendResult(userId, targetDate, sent, status, payload.TeamsDestination, payload.PlannedTaskCount);
    }

    private TimeZoneInfo ResolveTimeZone()
    {
        var configured = config["Notifications:DefaultTimeZone"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(configured);
            }
            catch (TimeZoneNotFoundException)
            {
                logger.LogWarning("Configured timezone {TimeZone} was not found. Falling back to local timezone.", configured);
            }
            catch (InvalidTimeZoneException)
            {
                logger.LogWarning("Configured timezone {TimeZone} was invalid. Falling back to local timezone.", configured);
            }
        }

        return TimeZoneInfo.Local;
    }
}

public sealed class DailyDigestScheduler(
    RoraQuestService service,
    DailyDigestDispatcher dispatcher,
    IConfiguration config,
    ILogger<DailyDigestScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cadenceSeconds = config.GetValue("Notifications:DailyDigestPollSeconds", 60);
        if (cadenceSeconds < 15) cadenceSeconds = 15;
        var cadence = TimeSpan.FromSeconds(cadenceSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunTick(stoppingToken);
            await Task.Delay(cadence, stoppingToken);
        }
    }

    private async Task RunTick(CancellationToken ct)
    {
        var users = service.GetKnownUserIds();
        if (users.Count == 0) return;

        foreach (var userId in users)
        {
            try
            {
                await dispatcher.SendForUserAsync(userId, null, false, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Daily digest scheduler failed for user {UserId}.", userId);
            }
        }
    }
}
