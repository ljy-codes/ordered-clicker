namespace OrderedClicker.Services;

public sealed class ProfileConflictException : IOException
{
    public ProfileConflictException(string message)
        : base(message)
    {
    }

    public ProfileConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
