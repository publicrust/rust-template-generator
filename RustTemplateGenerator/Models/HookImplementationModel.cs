using System.Text.Json.Serialization;

namespace RustTemplateGenerator.Models;

public class HookImplementationModel
{
    [JsonPropertyName("HookSignature")]
    public required string HookSignature { get; set; }

    [JsonPropertyName("MethodSignature")]
    public required string MethodSignature { get; set; }

    [JsonPropertyName("MethodSourseCode")]
    public required string MethodSourceCode { get; set; }

    [JsonPropertyName("ClassName")]
    public required string MethodClassName { get; set; }

    [JsonPropertyName("HookLineInvoke")]
    public int HookLineInvoke { get; set; }
} 