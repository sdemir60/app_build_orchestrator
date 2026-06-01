using System.Text.Json;

namespace BuildOrchestrator.Contracts;

/// <summary>Shared JSON settings so the App and Worker serialize identically.</summary>
public static class IpcJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string SerializeCommand(WorkerCommand command)
        => JsonSerializer.Serialize(command, Options);

    public static WorkerCommand? DeserializeCommand(string json)
        => JsonSerializer.Deserialize<WorkerCommand>(json, Options);

    public static string SerializeEvent(WorkerEvent evt)
        => JsonSerializer.Serialize(evt, Options);

    public static WorkerEvent? DeserializeEvent(string json)
        => JsonSerializer.Deserialize<WorkerEvent>(json, Options);
}
