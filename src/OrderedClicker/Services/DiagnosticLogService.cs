using System.Text;
using System.Text.Json;

namespace OrderedClicker.Services;

public sealed class DiagnosticLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _applicationVersion;

    public DiagnosticLogService(string diagnosticsDirectory, string? applicationVersion = null)
    {
        DiagnosticsDirectory = Path.GetFullPath(diagnosticsDirectory);
        _applicationVersion = applicationVersion
                              ?? typeof(DiagnosticLogService).Assembly
                                  .GetName().Version?.ToString()
                              ?? "unknown";
    }

    public DiagnosticLogService(AppDataPaths paths)
        : this(paths?.Diagnostics ?? throw new ArgumentNullException(nameof(paths)))
    {
    }

    public string DiagnosticsDirectory { get; }

    public string Write(
        string operation,
        Exception exception,
        IReadOnlyDictionary<string, object?>? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(exception);
        Directory.CreateDirectory(DiagnosticsDirectory);
        var path = Path.Combine(
            DiagnosticsDirectory,
            $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
        var temporary = path + ".tmp";
        var payload = new
        {
            CreatedAt = DateTimeOffset.Now,
            Operation = operation,
            ApplicationVersion = _applicationVersion,
            ExceptionType = exception.GetType().FullName,
            exception.Message,
            exception.StackTrace,
            Context = context
        };

        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(payload, JsonOptions),
                new UTF8Encoding(false));
            File.Move(temporary, path, true);
            return path;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
