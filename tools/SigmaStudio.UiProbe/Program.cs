using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;

var output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.OrdinalIgnoreCase))?[9..];
if (args.Any(a => string.Equals(a, "--capture-context-probe", StringComparison.OrdinalIgnoreCase)))
{
    ProbeCaptureContextMenu();
    return;
}
if (args.Any(a => string.Equals(a, "--capture-clipboard-probe", StringComparison.OrdinalIgnoreCase)))
{
    ProbeCaptureClipboard();
    return;
}

var root = AutomationElement.RootElement;
var sigmaWindows = root.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window))
    .Cast<AutomationElement>()
    .Where(window => (window.Current.Name ?? "").Contains("SigmaStudio", StringComparison.OrdinalIgnoreCase))
    .Select(BuildNode)
    .ToArray();
var result = new
{
    capturedAt = DateTimeOffset.UtcNow,
    process = "SStudio",
    windows = sigmaWindows
};
var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
if (string.IsNullOrWhiteSpace(output)) Console.WriteLine(json);
else await File.WriteAllTextAsync(output, json);

static object BuildNode(AutomationElement element)
{
    var children = element.FindAll(TreeScope.Children, Condition.TrueCondition).Cast<AutomationElement>().Take(500).Select(BuildNode).ToArray();
    return new
    {
        name = element.Current.Name,
        automationId = element.Current.AutomationId,
        controlType = element.Current.ControlType.ProgrammaticName,
        className = element.Current.ClassName,
        helpText = element.Current.HelpText,
        itemStatus = element.Current.ItemStatus,
        isEnabled = element.Current.IsEnabled,
        isKeyboardFocusable = element.Current.IsKeyboardFocusable,
        patterns = element.GetSupportedPatterns()
            .Select(pattern => pattern.ProgrammaticName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray(),
        children
    };
}

static void ProbeCaptureContextMenu()
{
    var capture = FindCaptureWindow();
    if (capture is null)
    {
        Console.WriteLine("{\"available\":false,\"reason\":\"captureWindow not found\"}");
        return;
    }

    var focusTarget = capture.FindFirst(
        TreeScope.Descendants,
        new PropertyCondition(AutomationElement.AutomationIdProperty, "treeViewAdv1")) ?? capture;
    focusTarget.SetFocus();
    SendContextMenuMessage(focusTarget.Current.NativeWindowHandle);

    var deadline = DateTime.UtcNow.AddSeconds(2);
    AutomationElement[] menuItems = [];
    while (DateTime.UtcNow < deadline)
    {
        menuItems = AutomationElement.RootElement
            .FindAll(TreeScope.Subtree, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Where(element => element.Current.ControlType == ControlType.MenuItem &&
                              !string.IsNullOrWhiteSpace(element.Current.Name))
            .ToArray();
        if (menuItems.Length > 0) break;
        Thread.Sleep(25);
    }

    var result = menuItems
        .Select(element => new
        {
            name = element.Current.Name,
            automationId = element.Current.AutomationId,
            className = element.Current.ClassName,
            controlType = element.Current.ControlType.ProgrammaticName,
            helpText = element.Current.HelpText,
            itemStatus = element.Current.ItemStatus,
            patterns = element.GetSupportedPatterns().Select(pattern => pattern.ProgrammaticName).ToArray()
        })
        .ToArray();

    Console.WriteLine(JsonSerializer.Serialize(new { available = true, items = result }, new JsonSerializerOptions { WriteIndented = true }));
    SendEscape();
}

static AutomationElement? FindCaptureWindow()
{
    var windows = AutomationElement.RootElement.FindAll(
        TreeScope.Children,
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
    var candidates = windows.Cast<AutomationElement>().Where(window =>
        (window.Current.Name ?? "").Contains("SigmaStudio", StringComparison.OrdinalIgnoreCase)).ToArray();
    var sigma = candidates.FirstOrDefault(IsSigmaStudioProcess);
    return sigma?.FindFirst(
        TreeScope.Descendants,
        new PropertyCondition(AutomationElement.AutomationIdProperty, "captureWindow"));
}

static bool IsSigmaStudioProcess(AutomationElement element)
{
    try
    {
        using var process = Process.GetProcessById(element.Current.ProcessId);
        return string.Equals(process.ProcessName, "SStudio", StringComparison.OrdinalIgnoreCase);
    }
    catch (ArgumentException)
    {
        return false;
    }
}

static void SendContextMenuMessage(int nativeWindowHandle)
{
    SendMessage((IntPtr)nativeWindowHandle, 0x007B, (IntPtr)nativeWindowHandle, new IntPtr(-1));
}

static void SendEscape() => KeybdEvent(0x1B, 0);

static void ProbeCaptureClipboard()
{
    object? result = null;
    Exception? error = null;
    var thread = new Thread(() =>
    {
        try
        {
            var capture = FindCaptureWindow();
            if (capture is null)
            {
                result = new { available = false, reason = "captureWindow not found" };
                return;
            }

            var focusTarget = capture.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "treeViewAdv1")) ?? capture;
            SetForegroundWindow(capture.Current.NativeWindowHandle);
            var before = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
            var sequenceBefore = GetClipboardSequenceNumber();
            focusTarget.SetFocus();
            var hasFocus = focusTarget.Current.HasKeyboardFocus;
            SendCtrlA();
            SendCtrlC();

            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline && GetClipboardSequenceNumber() == sequenceBefore)
                Thread.Sleep(25);

            var after = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
            var sequenceChanged = GetClipboardSequenceNumber() != sequenceBefore;
            if (before is null) System.Windows.Clipboard.Clear();
            else System.Windows.Clipboard.SetText(before);
            result = new
            {
                available = true,
                hasFocus,
                sequenceChanged,
                textLength = after?.Length ?? 0,
                text = after
            };
        }
        catch (Exception ex)
        {
            error = ex;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join(TimeSpan.FromSeconds(5));
    if (error is not null) throw error;
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
}

static void SendCtrlA()
{
    KeybdEvent(0x11, 0);
    KeybdEvent(0x41, 0);
    KeybdEvent(0x41, 2);
    KeybdEvent(0x11, 2);
}

static void SendCtrlC()
{
    KeybdEvent(0x11, 0);
    KeybdEvent(0x43, 0);
    KeybdEvent(0x43, 2);
    KeybdEvent(0x11, 2);
}

static void KeybdEvent(byte key, uint flags) => keybd_event(key, 0, flags, UIntPtr.Zero);

static uint GetClipboardSequenceNumber() => GetClipboardSequenceNumberNative();

static bool SetForegroundWindow(int nativeWindowHandle) => SetForegroundWindowNative((IntPtr)nativeWindowHandle);

[DllImport("user32.dll", EntryPoint = "GetClipboardSequenceNumber")]
static extern uint GetClipboardSequenceNumberNative();

[DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool SetForegroundWindowNative(IntPtr windowHandle);

[DllImport("user32.dll", EntryPoint = "SendMessage")]
static extern IntPtr SendMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

[DllImport("user32.dll", SetLastError = true)]
static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
