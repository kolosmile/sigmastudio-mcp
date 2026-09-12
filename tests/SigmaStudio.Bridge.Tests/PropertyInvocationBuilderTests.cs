using SigmaStudio.Bridge;
using System.Reflection;

namespace SigmaStudio.Bridge.Tests;

public sealed class PropertyInvocationBuilderTests
{
    [Fact]
    public void Get_control_value_uses_algorithm_repeat_and_control_parameters()
    {
        var args = PropertyInvocationBuilder.BuildGetControlValueArguments("Gain1", 0, 0, "Gain");

        Assert.Equal(["getControlValue", "Gain1"], args.Take(2));
        Assert.IsType<object[]>(args[2]);
        var propertyParams = Assert.IsType<object[]>(args[3]);
        Assert.Equal(3, propertyParams.Length);
        Assert.Equal(0, propertyParams[0]);
        Assert.Equal(0, propertyParams[1]);
        Assert.Equal("Gain", propertyParams[2]);
    }

    [Fact]
    public void Set_control_value_uses_the_documented_property_order()
    {
        var args = PropertyInvocationBuilder.BuildSetControlValueArguments("Gain1", 0, 0, "Gain", 0.25d);

        Assert.Equal("setControlValue", args[0]);
        Assert.Equal("Gain1", args[1]);
        var propertyParams = Assert.IsType<object[]>(args[2]);
        Assert.Equal([0, 0, "Gain", 0.25d], propertyParams);
    }

    [Fact]
    public void Exact_reflection_signatures_receive_the_expected_params_arrays()
    {
        var server = new FakeServer();
        var get = typeof(FakeServer).GetMethod(nameof(FakeServer.GET_OBJECT_PROPERTY))!;
        var getArgs = PropertyInvocationBuilder.BuildGetControlValueArguments("Mute1", 0, 0, "Mute");
        Assert.True((bool)get.Invoke(server, getArgs)!);
        Assert.Equal([0, 0, "Mute"], server.GetPropertyParams);
        Assert.Equal([false], (object[])getArgs[2]!);

        var set = typeof(FakeServer).GetMethod(nameof(FakeServer.SET_OBJECT_PROPERTY))!;
        var setArgs = PropertyInvocationBuilder.BuildSetControlValueArguments("Gain1", 0, 0, "Gain", 0.25d);
        Assert.True((bool)set.Invoke(server, setArgs)!);
        Assert.Equal([0, 0, "Gain", 0.25d], server.SetPropertyParams);
    }

    private sealed class FakeServer
    {
        public object[]? GetPropertyParams { get; private set; }
        public object[]? SetPropertyParams { get; private set; }

        public bool GET_OBJECT_PROPERTY(string opcode, string objectName, out object[] getPropVal, object[] propertyParams)
        {
            Assert.Equal("getControlValue", opcode);
            Assert.Equal("Mute1", objectName);
            GetPropertyParams = propertyParams;
            getPropVal = [false];
            return true;
        }

        public bool SET_OBJECT_PROPERTY(string opcode, string objectName, object[] propertyParams)
        {
            Assert.Equal("setControlValue", opcode);
            Assert.Equal("Gain1", objectName);
            SetPropertyParams = propertyParams;
            return true;
        }
    }
}
