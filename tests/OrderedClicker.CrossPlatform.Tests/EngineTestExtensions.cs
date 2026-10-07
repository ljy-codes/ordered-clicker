using OrderedClicker.Core;
using OrderedClicker.Models;
namespace OrderedClicker.Tests;
internal static class EngineTestExtensions
{
    // Explicit virtual desktop fixture replaces WinForms SystemInformation in existing engine tests.
    public static async Task ExecuteAsync(this ClickExecutionEngine engine,ClickProfile profile,AsyncPauseGate pause,
        IProgress<ExecutionProgress>? progress,CancellationToken token)
    {
        var plan=ExecutionPlanService.Create(profile,new ScreenBounds(0,0,7680,4320));
        var result=await engine.ExecuteAsync(plan,ExecutionCheckpoint.Start,pause,progress,token);
        if(result.Outcome==ExecutionOutcome.Stopped)throw new OperationCanceledException(token);
        if(result.Outcome==ExecutionOutcome.Failed)throw new InvalidOperationException(result.Message);
    }
}
