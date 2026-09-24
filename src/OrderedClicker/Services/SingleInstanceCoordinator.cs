using System.Security.Cryptography;
using System.Text;

namespace OrderedClicker.Services;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly CancellationTokenSource _listenerCancellation = new();
    private readonly CancellationTokenSource _ownershipCancellation = new();
    private readonly Thread _ownershipThread;
    private Task? _listenerTask;
    private bool _disposed;

    public SingleInstanceCoordinator(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        var key = CreateKey(identifier);
        _mutex = new Mutex(false, $@"Local\OrderedClicker.{key}.Mutex");
        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            $@"Local\OrderedClicker.{key}.Activate");
        using var ready = new ManualResetEventSlim();
        _ownershipThread = new Thread(() =>
        {
            try
            {
                IsOwner = _mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                IsOwner = true;
            }

            finally
            {
                ready.Set();
            }

            if (IsOwner)
            {
                _ownershipCancellation.Token.WaitHandle.WaitOne();
                _mutex.ReleaseMutex();
            }
        })
        {
            IsBackground = true,
            Name = "OrderedClicker single-instance owner"
        };
        _ownershipThread.Start();
        ready.Wait();
    }

    public bool IsOwner { get; private set; }

    public void StartListening(Action activationRequested)
    {
        ArgumentNullException.ThrowIfNull(activationRequested);
        if (!IsOwner)
        {
            throw new InvalidOperationException("只有主实例可以监听激活请求。");
        }

        _listenerTask ??= Task.Run(() =>
        {
            var handles = new[]
            {
                _activationEvent,
                _listenerCancellation.Token.WaitHandle
            };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                activationRequested();
            }
        });
    }

    public void SignalActivation()
    {
        _activationEvent.Set();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listenerCancellation.Cancel();
        _activationEvent.Set();
        try
        {
            _listenerTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }

        _ownershipCancellation.Cancel();
        _ownershipThread.Join(TimeSpan.FromSeconds(1));
        _ownershipCancellation.Dispose();
        _listenerCancellation.Dispose();
        _activationEvent.Dispose();
        _mutex.Dispose();
    }

    private static string CreateKey(string identifier)
    {
        var identity = $"{identifier}|{Environment.UserDomainName}|{Environment.UserName}";
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
    }
}
