namespace SigmaStudio.Bridge;

/// <summary>
/// Builds the late-bound SigmaStudioServer property calls from the documented
/// ObjectGetProperties/ObjectSetProperties contracts.  Keeping this in one
/// place makes the params object[] shape unit-testable without loading ADI's
/// legacy assembly.
/// </summary>
public static class PropertyInvocationBuilder
{
    public static object?[] BuildGetControlValueArguments(
        string objectName,
        int algorithmIndex,
        int repeatIndex,
        string controlName) =>
    [
        "getControlValue",
        objectName,
        Array.Empty<object>(),
        new object?[] { algorithmIndex, repeatIndex, controlName }
    ];

    public static object?[] BuildSetControlValueArguments(
        string objectName,
        int algorithmIndex,
        int repeatIndex,
        string controlName,
        object? value) =>
    [
        "setControlValue",
        objectName,
        new object?[] { algorithmIndex, repeatIndex, controlName, value }
    ];
}
