using System.Text.Json;
using SigmaStudio.Contracts;

namespace SigmaStudio.Core.Tests;

public sealed class ControlValueTests
{
    [Fact]
    public void Typed_control_values_cover_number_boolean_string_and_array()
    {
        var number = JsonDocument.Parse("0.25").RootElement.Clone();
        var boolean = JsonDocument.Parse("true").RootElement.Clone();
        var text = JsonDocument.Parse("\"Impulse\"").RootElement.Clone();
        var array = JsonDocument.Parse("[0.1,0.2]").RootElement.Clone();

        Assert.Equal("number", ControlValueJson.ValueType(number));
        Assert.Equal("boolean", ControlValueJson.ValueType(boolean));
        Assert.Equal("string", ControlValueJson.ValueType(text));
        Assert.Equal("array", ControlValueJson.ValueType(array));
        Assert.True(ControlValueJson.TryGetNumber(boolean, out var boolNumber));
        Assert.Equal(1, boolNumber);
    }

    [Fact]
    public void Set_control_input_keeps_a_json_value_on_the_wire()
    {
        var value = JsonDocument.Parse("true").RootElement.Clone();
        var input = new SetControlInput("Gain1", "Enabled", value, new MutationInput(1, "m1"));

        Assert.Equal(JsonValueKind.True, input.Value.ValueKind);
        Assert.Equal("true", input.Value.GetRawText());
    }
}
