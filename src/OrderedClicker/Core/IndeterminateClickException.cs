namespace OrderedClicker.Core;

public sealed class IndeterminateClickException(string message) : IOException(message);
