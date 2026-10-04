using System.Text.Json;
using System.Text.Json.Serialization;

namespace Adamantium.UI.Automation;

/// <summary>How requests and replies travel through the agent's pipe: one JSON object per line.</summary>
public static class AutomationProtocol
{
    /// <summary>The environment variable that names the agent's pipe. The agent starts only when it is set.</summary>
    public const string PipeVariable = "ADAM_AUTOMATION_PIPE";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Write(AutomationRequest request) => JsonSerializer.Serialize(request, Options);

    public static string Write(AutomationReply reply) => JsonSerializer.Serialize(reply, Options);

    /// <exception cref="JsonException">The line is not a request.</exception>
    public static AutomationRequest ReadRequest(string line) => JsonSerializer.Deserialize<AutomationRequest>(line, Options);

    /// <exception cref="JsonException">The line is not a reply.</exception>
    public static AutomationReply ReadReply(string line) => JsonSerializer.Deserialize<AutomationReply>(line, Options);
}
