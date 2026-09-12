using System.Text.Json;
using System.Text.Json.Serialization;

namespace SigmaStudio.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SigmaStudioState
{
    NoApplication,
    NoProject,
    DesignMode,
    ReadyCompiled,
    ActiveDownloaded,
    Busy,
    Error,
    Unknown
}

public enum GraphFreshness
{
    Fresh,
    Stale,
    Cached
}

public enum CatalogCompatibility
{
    Supported,
    Unsupported,
    Unknown
}

public sealed record PinDto(int Index, string Name, string Direction);

public sealed record ControlDto(
    string Name,
    double? Value,
    double? Min = null,
    double? Max = null,
    IReadOnlyList<string>? Enum = null,
    string? Unit = null,
    string? ControlId = null,
    string? Description = null,
    string? ValueType = null,
    string? UpdateMode = null,
    string? Source = null,
    GraphFreshness? Freshness = null,
    JsonElement? TypedValue = null);

public sealed record ParameterDto(string Name, string? Address, string? Format, double? Value);

public sealed record BlockDto(
    string Id,
    string ObjectName,
    string FullObjectName,
    string CatalogId,
    string DisplayName,
    IReadOnlyList<string> Algorithms,
    IReadOnlyList<PinDto> Inputs,
    IReadOnlyList<PinDto> Outputs,
    IReadOnlyList<ControlDto> Controls,
    IReadOnlyList<ParameterDto> Parameters,
    GraphPositionDto? Position = null,
    string? HierarchyPath = null,
    string? NativeObjectType = null,
    string? NativeObjectId = null);

public sealed record GraphPositionDto(double X, double Y);

public sealed record PinRefDto(string Block, int PinIndex, string PinName, string? BlockId = null);

public sealed record ConnectionDto(PinRefDto Source, PinRefDto Target, string? Id = null, string? NativeLinkId = null);

public sealed record ProjectGraphDto(
    ProjectIdentityDto Project,
    IReadOnlyList<BlockDto> Blocks,
    IReadOnlyList<ConnectionDto> Connections,
    long DesignRevision,
    GraphFreshness Freshness,
    IReadOnlyList<string>? Warnings = null,
    DateTimeOffset? ObservedAt = null,
    string? GraphFingerprint = null);

public sealed record ProjectIdentityDto(string? Path, string Chip, int SampleRateHz);

public sealed record ProjectStateDto(
    string? Path,
    string? Name,
    string Chip,
    int SampleRateHz,
    bool IsDirty,
    long DesignRevision,
    long RuntimeRevision,
    long? DeployedDesignRevision,
    SigmaStudioState SigmaStudioState,
    bool ReadyForMeasurement,
    string? CommandState = null,
    string? ObservedUiState = null,
    SigmaStudioState? NormalizedState = null);

public sealed record AutomationSnapshot(
    ProjectStateDto State,
    ProjectGraphDto Graph,
    IReadOnlyList<CaptureEntryDto> Capture,
    string Backend,
    bool Connected,
    string? CaptureWarning = null);

public sealed record CaptureEntryDto(
    long Sequence,
    DateTimeOffset Timestamp,
    string Direction,
    string Category,
    string Summary,
    string? Block = null,
    string? Control = null,
    JsonElement? Payload = null);

public sealed record CaptureCursorDto(long NextSequence);

public sealed record UiStateObservationDto(
    bool Available,
    string? Window,
    string? RawText,
    string? StatusControl,
    SigmaStudioState? NormalizedState);

public sealed record CatalogSourceDto(
    string Publisher,
    string RetrievedAt,
    string ContentHash,
    string Reference);

public sealed record CatalogBlockDto(
    string Id,
    string Name,
    string ToolboxPath,
    string Category,
    IReadOnlyList<string> CoresSupported,
    CatalogCompatibility Adau1701Compatibility,
    IReadOnlyList<PinDto> Inputs,
    IReadOnlyList<PinDto> Outputs,
    IReadOnlyList<ControlDto> Controls,
    IReadOnlyList<ParameterDto> DspParameters,
    bool GrowSupported,
    bool AddAlgorithmSupported,
    IReadOnlyDictionary<string, double>? ResourceUsage,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> Automation,
    CatalogSourceDto Source,
    string? Description = null,
    bool? AvailableInInstalledSigmaStudio = null);

public sealed record OperationErrorDto(string Code, string Message, IReadOnlyDictionary<string, object?>? Details = null);

public sealed record SigmaToolResult<T>(
    bool Ok,
    string OperationId,
    long DesignRevision,
    long RuntimeRevision,
    SigmaStudioState SigmaStudioState,
    bool ReadyForMeasurement,
    IReadOnlyList<string> Warnings,
    T? Data,
    OperationErrorDto? Error = null,
    bool RollbackAttempted = false,
    bool RollbackSucceeded = false);

public sealed record MutationInput(long ExpectedDesignRevision, string MutationId);

public sealed record ControlChangeInput
{
    public string Control { get; init; }
    public JsonElement Value { get; init; }

    [JsonConstructor]
    public ControlChangeInput(string control, JsonElement value)
    {
        Control = control;
        Value = value.Clone();
    }

    public ControlChangeInput(string control, double value)
        : this(control, ControlValueJson.Number(value))
    {
    }
}

public sealed record ProjectOpenInput(string Path);

public sealed record ProjectCreateInput(string Path, int SampleRateHz = 48000);

public sealed record ProjectSaveAsInput(string Path, bool Overwrite = false);

public sealed record GraphGetInput(string Refresh = "cached");

public sealed record GraphTransactionInput(
    long ExpectedDesignRevision,
    IReadOnlyList<JsonElement> Operations,
    bool Validate = true,
    bool Deploy = false,
    string? MutationId = null);

public sealed record GraphDiffDto(
    long PreviousRevision,
    long NewRevision,
    IReadOnlyList<string> BlocksAdded,
    IReadOnlyList<string> BlocksRemoved,
    IReadOnlyList<string> ConnectionsAdded,
    IReadOnlyList<string> ConnectionsRemoved,
    IReadOnlyList<string> ControlsChanged);

public sealed record GraphTransactionResultDto(
    GraphDiffDto Diff,
    ProjectGraphDto Graph,
    bool Validated,
    bool Deployed,
    bool ProjectStateUncertain = false);

public sealed record BlockAddInput(
    string CatalogId,
    string ObjectName,
    MutationInput Mutation,
    double? X = null,
    double? Y = null);

public sealed record BlockRemoveInput(string Block, MutationInput Mutation);

public sealed record BlockRenameInput(string Block, string NewName, MutationInput Mutation);

public sealed record SetControlInput
{
    public string Block { get; init; }
    public string Control { get; init; }
    public JsonElement Value { get; init; }
    public MutationInput Mutation { get; init; }

    [JsonConstructor]
    public SetControlInput(string block, string control, JsonElement value, MutationInput mutation)
    {
        Block = block;
        Control = control;
        Value = value.Clone();
        Mutation = mutation;
    }

    public SetControlInput(string block, string control, double value, MutationInput mutation)
        : this(block, control, ControlValueJson.Number(value), mutation)
    {
    }
}

public sealed record SetControlsInput(string Block, IReadOnlyList<ControlChangeInput> Changes, MutationInput Mutation);

public sealed record ConnectionInput(
    string SourceBlock,
    int SourcePinIndex,
    string SourcePinName,
    string TargetBlock,
    int TargetPinIndex,
    string TargetPinName,
    MutationInput Mutation);

public sealed record ConnectionRemoveInput(ConnectionDto Connection, MutationInput Mutation);

public sealed record CaptureGetInput(long? AfterSequence = null, int Limit = 100, bool IncludeRaw = false);

public sealed record PropertyProbeGetControlValueInput(
    string ObjectName,
    string ControlName,
    int AlgorithmIndex = 0,
    int RepeatIndex = 0);

public sealed record PropertyProbeSetControlValueInput(
    string ObjectName,
    string ControlName,
    JsonElement Value,
    int AlgorithmIndex = 0,
    int RepeatIndex = 0);

public sealed record RawAccessOptions(bool EnableRawParameterAccess = false, bool EnableRawRegisterAccess = false);

public sealed record BridgeRequest(
    string Id,
    string Method,
    JsonElement? Params = null,
    int TimeoutMs = 30000);

public sealed record BridgeError(string Code, string Message, IReadOnlyDictionary<string, object?>? Details = null);

public sealed record BridgeResponse(
    string Id,
    bool Ok,
    JsonElement? Result = null,
    BridgeError? Error = null,
    long DurationMs = 0);

public sealed record BridgeCapabilities(
    string BridgeVersion,
    string Backend,
    bool SigmaStudioServerLoaded,
    bool UiAutomationAvailable,
    IReadOnlyList<string> Methods);

public sealed record DiagnosticsDto(
    string Os,
    string Dotnet,
    string Backend,
    bool SigmaStudioServerFound,
    bool SigmaStudioRunning,
    bool BridgeConnected,
    string? SigmaStudioPath,
    string? SigmaStudioServerPath,
    string? UiAutomationStatus,
    string CatalogVersion,
    IReadOnlyList<string> Warnings);

public sealed record CatalogDiscoveryDto(
    bool AvailableInInstalledSigmaStudio,
    string Source,
    IReadOnlyList<string> VerifiedAutomationMethods,
    IReadOnlyList<string> Warnings);

public static class ControlValueJson
{
    public static JsonElement Number(double value) =>
        JsonDocument.Parse(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).RootElement.Clone();

    public static bool TryGetNumber(JsonElement value, out double number)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number)) return true;
        if (value.ValueKind == JsonValueKind.True)
        {
            number = 1;
            return true;
        }
        if (value.ValueKind == JsonValueKind.False)
        {
            number = 0;
            return true;
        }
        number = default;
        return false;
    }

    public static string ValueType(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.String => "string",
        JsonValueKind.Array => "array",
        JsonValueKind.Object => "object",
        _ => "null"
    };
}
