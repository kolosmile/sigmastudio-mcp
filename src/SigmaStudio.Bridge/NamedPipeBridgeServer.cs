using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using SigmaStudio.Contracts;

namespace SigmaStudio.Bridge;

public sealed class NamedPipeBridgeServer
{
    private const int MaxMessageBytes = 16 * 1024 * 1024;
    private readonly BridgeOptions _options;
    private readonly SigmaStudioServerAdapter _adapter;
    private readonly UiAutomationObserver _observer;
    private readonly StaDispatcher _sta = new();

    public NamedPipeBridgeServer(BridgeOptions options, SigmaStudioServerAdapter adapter, UiAutomationObserver observer)
    {
        _options = options;
        _adapter = adapter;
        _observer = observer;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var server = CreateServer();
            try
            {
                await server.WaitForConnectionAsync();
                await ServeClientAsync(server, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                // A disconnected client must not terminate the Bridge.
            }
        }
        _sta.Dispose();
    }

    private async Task ServeClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        while (stream is { CanRead: true } && !cancellationToken.IsCancellationRequested)
        {
            var requestBytes = await PipeFrame.ReadAsync(stream, cancellationToken);
            if (requestBytes is null) break;
            BridgeRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<BridgeRequest>(requestBytes, JsonOptions);
                if (request is null) throw new JsonException("Request is null.");
            }
            catch (Exception ex)
            {
                await PipeFrame.WriteAsync(stream, JsonSerializer.SerializeToUtf8Bytes(new BridgeResponse("unknown", false, null, new BridgeError("INTERNAL_ERROR", ex.Message))), cancellationToken);
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            var response = await DispatchAsync(request, cancellationToken);
            stopwatch.Stop();
            await PipeFrame.WriteAsync(stream, JsonSerializer.SerializeToUtf8Bytes(response with { DurationMs = stopwatch.ElapsedMilliseconds }, JsonOptions), cancellationToken);
        }
    }

    private Task<BridgeResponse> DispatchAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        return _sta.InvokeAsync(() =>
        {
            try
            {
                return request.Method switch
                {
                    "bridge.ping" => Success(request.Id, new { pong = true }),
                    "bridge.get_version" => Success(request.Id, new { version = "0.1.0", targetFramework = ".NET 8 Windows" }),
                    "bridge.get_capabilities" => Success(request.Id, new BridgeCapabilities("0.1.0", _adapter.BackendName, _adapter.IsLoaded, _observer.IsAvailable, _adapter.Methods)),
                    "bridge.probe" => Success(request.Id, _adapter.Probe()),
                    "bridge.get_snapshot" => Success(request.Id, MergeUiState(_adapter.GetSnapshot() with
                    {
                        Capture = _observer.ReadCaptureEntries(),
                        CaptureWarning = _observer.CaptureWarning
                    }, _observer.ReadStatusObservation())),
                    "bridge.ui_status" => Success(request.Id, _observer.ReadStatus()),
                    "bridge.capture_get" => Success(request.Id, _observer.ReadCapture()),
                    _ => DispatchAdapter(request)
                };
            }
            catch (Exception ex)
            {
                return new BridgeResponse(request.Id, false, null, new BridgeError("INTERNAL_ERROR", ex.Message));
            }
        }, cancellationToken);
    }

    private BridgeResponse DispatchAdapter(BridgeRequest request)
    {
        var result = _adapter.Execute(request.Method, request.Params);
        return result.Ok ? Success(request.Id, result.Result) : new BridgeResponse(request.Id, false, null, result.Error);
    }

    private static AutomationSnapshot MergeUiState(AutomationSnapshot snapshot, UiStateObservationDto observed)
    {
        var commandState = snapshot.State.SigmaStudioState.ToString();
        var normalizedState = observed.Available ? observed.NormalizedState : null;
        var ready = normalizedState == SigmaStudioState.ActiveDownloaded &&
                    snapshot.State.DeployedDesignRevision is not null &&
                    snapshot.State.DeployedDesignRevision == snapshot.State.DesignRevision;
        var state = snapshot.State with
        {
            SigmaStudioState = normalizedState ?? SigmaStudioState.Unknown,
            ReadyForMeasurement = ready,
            CommandState = commandState,
            ObservedUiState = observed.RawText,
            NormalizedState = normalizedState
        };
        return snapshot with { State = state };
    }

    private NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        var identity = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current Windows user SID is unavailable.");
        security.AddAccessRule(new PipeAccessRule(identity, PipeAccessRights.ReadWrite, AccessControlType.Allow));
#if NET48
        return new NamedPipeServerStream(_options.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
#else
        return NamedPipeServerStreamAcl.Create(_options.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
#endif
    }

    private static BridgeResponse Success(string id, object? result) => new(id, true, JsonSerializer.SerializeToElement(result, JsonOptions));
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal static class PipeFrame
{
    private const int MaxMessageBytes = 16 * 1024 * 1024;

    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var prefix = new byte[4];
        if (!await ReadExactlyAsync(stream, prefix, cancellationToken)) return null;
        var length = BitConverter.ToInt32(prefix, 0);
        if (length < 0 || length > MaxMessageBytes) throw new InvalidDataException("Bridge message exceeds 16 MiB.");
        var payload = new byte[length];
        if (!await ReadExactlyAsync(stream, payload, cancellationToken)) return null;
        return payload;
    }

    public static async Task WriteAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        if (payload.Length > MaxMessageBytes) throw new InvalidDataException("Bridge message exceeds 16 MiB.");
        var prefix = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(prefix, 0, prefix.Length, cancellationToken);
        await stream.WriteAsync(payload, 0, payload.Length, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer, offset, buffer.Length - offset, cancellationToken);
            if (count == 0) return false;
            offset += count;
        }
        return true;
    }
}
