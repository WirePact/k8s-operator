namespace Operator.Services;

public abstract class TimedService  : IHostedService, IDisposable
{
    private Timer? _timer;
    private readonly TimeSpan _interval;

    protected TimedService(TimeSpan interval)
    {
        _interval = interval;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(Execute, null, TimeSpan.Zero, _interval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    protected abstract void Execute(object? _);
}
