global using Xunit;
using System.Text.Json;
using SigmaStudio.Contracts;

namespace SigmaStudio.Mcp.ContractTests;

public sealed class ContractTests
{
    [Fact]
    public void Bridge_request_round_trips_with_length_prefixed_shape()
    {
        var request = new BridgeRequest("id", "bridge.ping", JsonSerializer.SerializeToElement(new { value = 1 }), 30000);
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var parsed = JsonSerializer.Deserialize<BridgeRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(request.Id, parsed!.Id);
        Assert.Equal("bridge.ping", parsed.Method);
    }
}
