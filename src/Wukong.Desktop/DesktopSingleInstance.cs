using System.Threading;

namespace Wukong.Desktop;

public sealed class DesktopSingleInstance : IDisposable
{
    public const string DefaultName = "Wukong.Desktop.SingleInstance.v1";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationSignal;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listener;
    private bool _disposed;

    private DesktopSingleInstance(string name)
    {
        _mutex = new Mutex(true, $"Local\\{name}.Mutex", out var createdNew);
        IsPrimary = createdNew;
        _activationSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $"Local\\{name}.Activate");
    }

    public bool IsPrimary { get; }

    public static DesktopSingleInstance Acquire(string? name = null) =>
        new(string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim());

    public void SignalPrimary() => _activationSignal.Set();

    public void StartListening(Action activationRequested)
    {
        ArgumentNullException.ThrowIfNull(activationRequested);
        if (!IsPrimary || _listener is not null)
            return;

        _listener = Task.Run(() =>
        {
            while (!_cancellation.IsCancellationRequested)
            {
                _activationSignal.WaitOne();
                if (!_cancellation.IsCancellationRequested)
                {
                    try { activationRequested(); }
                    catch (InvalidOperationException) when (_cancellation.IsCancellationRequested) { }
                }
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cancellation.Cancel();
        _activationSignal.Set();
        try { _listener?.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
        if (IsPrimary)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _activationSignal.Dispose();
        _mutex.Dispose();
        _cancellation.Dispose();
    }
}
