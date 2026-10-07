namespace RoraQuest.Api.Progress;

public sealed class ProgressCaptureLifecycle(ProgressLedger ledger, ILogger<ProgressCaptureLifecycle> logger) : IHostedService, IDisposable
{
    private ITimer? _timer;
    private int _stopped;
    private readonly object _lifecycleGate = new();
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ledger.Start();
        _timer = ledger.Clock.CreateTimer(_ =>
        {
            lock (_lifecycleGate)
            {
                if (Volatile.Read(ref _stopped) != 0) return;
                try { ledger.Heartbeat(); }
                catch (Exception ex) { logger.LogError(ex, "Progress certification failed; the uncertain interval will not be extended"); }
            }
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return Task.CompletedTask;
        _timer?.Dispose();
        lock (_lifecycleGate)
        {
            try { ledger.Stop(); }
            catch (Exception ex) { logger.LogError(ex, "Final Progress certification failed; the finite prior boundary is retained"); }
        }
        return Task.CompletedTask;
    }
    public void Dispose() => _timer?.Dispose();
}
