using OrderedClicker.Models;

namespace OrderedClicker.Services;

public sealed record HotKeyRegistration(int Id, string Name, HotKeyBinding Binding);

public sealed record HotKeyRegistrationResult(bool Success, IReadOnlyList<string> Errors)
{
    public string Message => string.Join(Environment.NewLine, Errors);

    public static HotKeyRegistrationResult Successful { get; } = new(true, []);
}

public interface IHotKeyRegistrar : IDisposable
{
    void Register(HotKeyRegistration registration);

    void Unregister(int id);
}

public sealed class HotKeyRegistrationCoordinator : IDisposable
{
    private readonly IHotKeyRegistrar _registrar;
    private List<HotKeyRegistration> _registeredBindings = [];
    private bool _disposed;

    public HotKeyRegistrationCoordinator(IHotKeyRegistrar registrar)
    {
        _registrar = registrar;
    }

    public IReadOnlyList<HotKeyRegistration> RegisteredBindings => _registeredBindings;

    public HotKeyRegistrationResult RegisterInitial(
        IReadOnlyList<HotKeyRegistration> bindings)
    {
        ThrowIfDisposed();
        foreach (var binding in _registeredBindings.ToList())
        {
            try
            {
                _registrar.Unregister(binding.Id);
            }
            catch
            {
                // Initial registration is only used once by the main form.
            }
        }

        _registeredBindings = [];

        var errors = new List<string>();
        foreach (var binding in bindings)
        {
            try
            {
                _registrar.Register(binding);
                _registeredBindings.Add(binding);
            }
            catch (Exception exception)
            {
                errors.Add($"{binding.Name}：{exception.Message}");
            }
        }

        return errors.Count == 0
            ? HotKeyRegistrationResult.Successful
            : new HotKeyRegistrationResult(false, errors);
    }

    public HotKeyRegistrationResult Replace(
        IReadOnlyList<HotKeyRegistration> bindings)
    {
        ThrowIfDisposed();
        var previous = _registeredBindings.ToList();
        var active = previous.ToDictionary(item => item.Id);
        var errors = new List<string>();
        var removedPrevious = new List<HotKeyRegistration>();

        foreach (var binding in previous)
        {
            try
            {
                _registrar.Unregister(binding.Id);
                active.Remove(binding.Id);
                removedPrevious.Add(binding);
            }
            catch (Exception exception)
            {
                errors.Add($"注销{binding.Name}快捷键失败：{exception.Message}");
            }
        }

        if (errors.Count > 0)
        {
            RestoreBindings(removedPrevious, active, errors);
            _registeredBindings = OrderActiveBindings(active, previous);
            return new HotKeyRegistrationResult(false, errors);
        }

        foreach (var binding in bindings)
        {
            try
            {
                _registrar.Register(binding);
                active[binding.Id] = binding;
            }
            catch (Exception exception)
            {
                errors.Add($"{binding.Name}：{exception.Message}");
                RollBackReplacement(active, previous, errors);
                _registeredBindings = OrderActiveBindings(active, previous);
                return new HotKeyRegistrationResult(false, errors);
            }
        }

        _registeredBindings = OrderActiveBindings(active, bindings);
        return HotKeyRegistrationResult.Successful;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var binding in _registeredBindings.ToList())
        {
            try
            {
                _registrar.Unregister(binding.Id);
            }
            catch
            {
                // Window destruction releases remaining registrations.
            }
        }

        _registeredBindings = [];
        _registrar.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void RollBackReplacement(
        IDictionary<int, HotKeyRegistration> active,
        IReadOnlyList<HotKeyRegistration> previous,
        ICollection<string> errors)
    {
        foreach (var binding in active.Values.ToList())
        {
            try
            {
                _registrar.Unregister(binding.Id);
                active.Remove(binding.Id);
            }
            catch (Exception exception)
            {
                errors.Add($"释放新{binding.Name}快捷键失败：{exception.Message}");
            }
        }

        RestoreBindings(
            previous.Where(binding => !active.ContainsKey(binding.Id)),
            active,
            errors);
    }

    private void RestoreBindings(
        IEnumerable<HotKeyRegistration> bindings,
        IDictionary<int, HotKeyRegistration> active,
        ICollection<string> errors)
    {
        foreach (var binding in bindings)
        {
            try
            {
                _registrar.Register(binding);
                active[binding.Id] = binding;
            }
            catch (Exception exception)
            {
                errors.Add($"恢复{binding.Name}快捷键失败：{exception.Message}");
            }
        }
    }

    private static List<HotKeyRegistration> OrderActiveBindings(
        IReadOnlyDictionary<int, HotKeyRegistration> active,
        IEnumerable<HotKeyRegistration> preferredOrder)
    {
        var result = preferredOrder
            .Where(binding =>
                active.TryGetValue(binding.Id, out var current)
                && current == binding)
            .ToList();
        result.AddRange(active.Values.Where(binding => !result.Contains(binding)));
        return result;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
