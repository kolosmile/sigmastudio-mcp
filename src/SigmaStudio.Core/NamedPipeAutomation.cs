using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SigmaStudio.Contracts;

namespace SigmaStudio.Core;

public sealed class NamedPipeSigmaStudioAutomation : ISigmaStudioAutomation
{
    private const int MaxMessageBytes = 16 * 1024 * 1024;
    private readonly string _pipeName;
    private readonly TimeSpan _connectTimeout;

    public NamedPipeSigmaStudioAutomation(string? pipeName = null, TimeSpan? connectTimeout = null)
    {
        _pipeName = pipeName ?? PipeNameForCurrentUser();
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(3);
    }

    public string BackendName => "NamedPipeBridge";

    public async Task<AutomationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync("bridge.get_snapshot", null, cancellationToken);
        if (!response.Ok) throw new InvalidOperationException(response.Error?.Message ?? "Bridge snapshot failed.");
        return response.Result!.Value.Deserialize<AutomationSnapshot>(JsonOptions) ?? throw new InvalidOperationException("Bridge returned an invalid snapshot.");
    }

    public async Task<AutomationResult> ExecuteAsync(AutomationCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAsync(command.Name, command.Parameters, cancellationToken);
            if (response.Ok) return AutomationResult.Success(response.Result);
            return AutomationResult.Failure(response.Error?.Code ?? "BRIDGE_DISCONNECTED", response.Error?.Message ?? "Bridge operation failed.", response.Error?.Details);
        }
        catch (TimeoutException ex)
        {
            return AutomationResult.Failure("BRIDGE_NOT_RUNNING", ex.Message);
        }
        catch (Exception ex)
        {
            return AutomationResult.Failure("BRIDGE_DISCONNECTED", ex.Message);
        }
    }

    private async Task<BridgeResponse> SendAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(_connectTimeout, cancellationToken);
        var request = new BridgeRequest(Guid.NewGuid().ToString("N"), method, parameters is null ? null : JsonSerializer.SerializeToElement(parameters, JsonOptions));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
        await WriteFrameAsync(client, bytes, cancellationToken);
        var responseBytes = await ReadFrameAsync(client, cancellationToken) ?? throw new EndOfStreamException("Bridge closed the pipe.");
        return JsonSerializer.Deserialize<BridgeResponse>(responseBytes, JsonOptions) ?? throw new InvalidDataException("Bridge returned an invalid response.");
    }

    private static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var prefix = new byte[4];
        if (!await ReadExactlyAsync(stream, prefix, ct)) return null;
        var length = BitConverter.ToInt32(prefix, 0);
        if (length < 0 || length > MaxMessageBytes) throw new InvalidDataException("Bridge message exceeds 16 MiB.");
        var payload = new byte[length];
        return await ReadExactlyAsync(stream, payload, ct) ? payload : null;
    }

    private static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken ct)
    {
        if (payload.Length > MaxMessageBytes) throw new InvalidDataException("Bridge message exceeds 16 MiB.");
        await stream.WriteAsync(BitConverter.GetBytes(payload.Length), ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }

    public static string PipeNameForCurrentUser()
    {
        var identity = OperatingSystem.IsWindows() ? System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName : Environment.UserName;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()[..16];
        return $"SigmaStudioMcp.{hash}";
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
