using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildOrchestrator.Contracts;

/// <summary>
/// Direction of a message in the Worker↔UI protocol (Section 8).
/// </summary>
public enum MessageKind
{
    Command,
    Event
}

/// <summary>
/// Well-known command names sent UI → Worker (Section 8).
/// </summary>
public static class Commands
{
    public const string SyncWorkspace = "syncWorkspace";
    public const string Reanalyze = "reanalyze";
    public const string ListBranches = "listBranches";
    public const string SelectBranch = "selectBranch";
    public const string StartRun = "startRun";
    public const string StopRun = "stopRun";
    public const string OpenPath = "openPath";
    public const string OpenInVs = "openInVS";
    public const string Shutdown = "shutdown";
}

/// <summary>
/// Well-known event names sent Worker → UI (Section 8).
/// </summary>
public static class Events
{
    public const string SyncProgress = "syncProgress";
    public const string SyncCompleted = "syncCompleted";
    public const string RunStarted = "runStarted";
    public const string ProjectStarted = "projectStarted";
    public const string ProjectLog = "projectLog";
    public const string ProjectSucceeded = "projectSucceeded";
    public const string ProjectFailed = "projectFailed";
    public const string ProjectSkipped = "projectSkipped";
    public const string RunCompleted = "runCompleted";
    public const string RunCancelled = "runCancelled";
    public const string Error = "error";
}

/// <summary>
/// JSON envelope exchanged over the named pipe / stdio channel.
/// The <see cref="Payload"/> is a raw JSON element deserialized on demand based on <see cref="Name"/>.
/// </summary>
public sealed class Message
{
    public MessageKind Kind { get; set; }

    /// <summary>Command or event name (see <see cref="Commands"/> / <see cref="Events"/>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Correlation id; events tied to a command/run echo the same id.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Opaque, strongly-typed-on-demand payload.</summary>
    public JsonElement? Payload { get; set; }

    public static Message Command(string name, object? payload = null, string? correlationId = null)
        => Create(MessageKind.Command, name, payload, correlationId);

    public static Message Event(string name, object? payload = null, string? correlationId = null)
        => Create(MessageKind.Event, name, payload, correlationId);

    private static Message Create(MessageKind kind, string name, object? payload, string? correlationId)
    {
        JsonElement? element = null;
        if (payload is not null)
        {
            element = JsonSerializer.SerializeToElement(payload, ProtocolJson.Options);
        }

        return new Message
        {
            Kind = kind,
            Name = name,
            CorrelationId = correlationId,
            Payload = element
        };
    }

    /// <summary>Deserialize the payload into <typeparamref name="T"/>, or default when absent.</summary>
    public T? GetPayload<T>()
        => Payload is null ? default : Payload.Value.Deserialize<T>(ProtocolJson.Options);
}

/// <summary>Shared JSON options for the protocol (camelCase, enums as strings).</summary>
public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
