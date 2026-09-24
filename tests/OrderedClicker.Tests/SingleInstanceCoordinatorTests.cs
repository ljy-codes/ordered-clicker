using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class SingleInstanceCoordinatorTests
{
    public static async Task RunAsync()
    {
        var identifier = $"OrderedClicker.Tests.{Guid.NewGuid():N}";
        using var first = new SingleInstanceCoordinator(identifier);
        using var second = new SingleInstanceCoordinator(identifier);
        var activated = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        TestAssert.True(first.IsOwner, "第一个实例应取得所有权");
        TestAssert.True(!second.IsOwner, "第二个实例不得取得所有权");
        first.StartListening(() => activated.TrySetResult());
        second.SignalActivation();

        await activated.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
