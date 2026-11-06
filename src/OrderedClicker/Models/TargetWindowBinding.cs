namespace OrderedClicker.Models;

public sealed class TargetWindowBinding
{
    public string Platform { get; set; } = string.Empty;

    public string ApplicationId { get; set; } = string.Empty;

    public string WindowTitle { get; set; } = string.Empty;
}
