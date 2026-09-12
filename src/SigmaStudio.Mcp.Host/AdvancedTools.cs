using System.ComponentModel;
using ModelContextProtocol.Server;
using SigmaStudio.Contracts;
using SigmaStudio.Core;

namespace SigmaStudio.Mcp.Host;

// Registered only when the corresponding feature flags are enabled. The high-level
// implementation deliberately does not guess fixed-point formats or raw addresses;
// these tools remain guarded until export-derived raw access is wired to a verified backend.
[McpServerToolType]
public sealed class AdvancedTools
{
    private readonly SigmaRuntime _runtime;
    public AdvancedTools(SigmaRuntime runtime) => _runtime = runtime;

    [McpServerTool(Name = "sigma_parameter_read", ReadOnly = true), Description("Read a raw DSP parameter only when raw parameter access is explicitly enabled.")]
    public object ParameterRead() => Disabled(_runtime.RawParameterAccessEnabled, "sigma_parameter_read");

    [McpServerTool(Name = "sigma_parameter_write"), Description("Write a raw DSP parameter only when raw parameter access is explicitly enabled.")]
    public object ParameterWrite() => Disabled(_runtime.RawParameterAccessEnabled, "sigma_parameter_write");

    [McpServerTool(Name = "sigma_parameter_safeload_write"), Description("Write a raw DSP parameter through safeload only when explicitly enabled.")]
    public object ParameterSafeloadWrite() => Disabled(_runtime.RawParameterAccessEnabled, "sigma_parameter_safeload_write");

    [McpServerTool(Name = "sigma_register_read", ReadOnly = true), Description("Read a raw DSP register only when raw register access is explicitly enabled.")]
    public object RegisterRead() => Disabled(_runtime.RawRegisterAccessEnabled, "sigma_register_read");

    [McpServerTool(Name = "sigma_register_write"), Description("Write a raw DSP register only when raw register access is explicitly enabled.")]
    public object RegisterWrite() => Disabled(_runtime.RawRegisterAccessEnabled, "sigma_register_write");

    private static object Disabled(bool enabled, string operation) => new
    {
        ok = false,
        operationId = Guid.NewGuid().ToString("N"),
        error = new { code = enabled ? "INTERNAL_ERROR" : "RAW_ACCESS_DISABLED", message = $"{operation} is not implemented without export-derived address/format verification." }
    };
}
