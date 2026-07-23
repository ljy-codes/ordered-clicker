using OrderedClicker.Models;

namespace OrderedClicker.Services;

public interface IScreenSampler
{
    Task<byte[]> SampleAsync(
        ScreenBounds region,
        CancellationToken cancellationToken);
}
