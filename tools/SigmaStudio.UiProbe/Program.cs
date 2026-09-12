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
if (args.Any(a => string.Equals(a, "--capture-row-context-probe", StringComparison.OrdinalIgnoreCase)))
{
    ProbeCaptureRowContextMenu();
    return;
}
if (args.Any(a => string.Equals(a, "--capture-mouse-context-probe", StringComparison.OrdinalIgnoreCase)))
{
    ProbeCaptureMouseContextMenu();
    return;
}
if (args.Any(a => string.Equals(a, "--capture-selected-context-probe", StringComparison.OrdinalIgnoreCase)))
{
    ProbeCaptureSelectedContextMenu();
    return;
}
if (args.Any(a => string.Equals(a, "--capture-selection-probe", StringComparison.OrdinalIgnoreCase)))
{
    ProbeCaptureSelection();
    return;
}
if (args.Any(a => string.Equals(a, "--capture-key-only-probe", StringComparison.OrdinalIgnoreCase)))
{
    ProbeCaptureKeyOnly();
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

static AutomationElement? FindSigmaWindow()
{
    var windows = AutomationElement.RootElement.FindAll(
        TreeScope.Children,
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
    return windows.Cast<AutomationElement>()
        .Where(window => (window.Current.Name ?? "").Contains("SigmaStudio", StringComparison.OrdinalIgnoreCase))
        .FirstOrDefault(IsSigmaStudioProcess);
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

static void ProbeCaptureRowContextMenu()
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
    var sigmaWindow = FindSigmaWindow();
    var foregroundSet = sigmaWindow is not null && SetForegroundWindow(sigmaWindow.Current.NativeWindowHandle);
    focusTarget.SetFocus();
    var steps = new List<string>();
    var captureBounds = capture.Current.BoundingRectangle;
    var focusBounds = focusTarget.Current.BoundingRectangle;
    steps.Add($"capture={captureBounds};focus={focusTarget.Current.AutomationId};focusBounds={focusBounds};hasFocus={focusTarget.Current.HasKeyboardFocus}");
    var hasClickablePoint = focusTarget.TryGetClickablePoint(out var clickablePoint);
    var rowX = hasClickablePoint ? (int)Math.Round(clickablePoint.X) : (int)Math.Round(focusBounds.X + Math.Min(80, Math.Max(10, focusBounds.Width / 4)));
    var rowY = hasClickablePoint ? (int)Math.Round(clickablePoint.Y) : (int)Math.Round(focusBounds.Y + Math.Min(42, Math.Max(30, focusBounds.Height / 4)));
    SetCursorPos(rowX, rowY);
    MouseEvent(0x0002); // left down
    MouseEvent(0x0004); // left up
    Thread.Sleep(100);
    SendNavigationKey(0x24, 0x47); // Home
    SendShiftEnd();
    SetCursorPos(rowX, rowY);
    MouseEvent(0x0008); // right down
    MouseEvent(0x0010); // right up
    var items = WaitForMenuItems(TimeSpan.FromSeconds(2));
    if (items.Length == 0)
    {
        var packedPoint = new IntPtr((rowY << 16) | (rowX & 0xFFFF));
        SendMessage((IntPtr)focusTarget.Current.NativeWindowHandle, 0x007B, (IntPtr)focusTarget.Current.NativeWindowHandle, packedPoint);
        items = WaitForMenuItems(TimeSpan.FromSeconds(2));
    }
    steps.Add($"home-shiftEnd-rightClick={items.Length};clickable={hasClickablePoint};point={rowX},{rowY}");
    var copyInvoked = false;
    string? clipboardText = null;
    var copyItem = items.FirstOrDefault(item => item.Current.Name.Contains("copy", StringComparison.OrdinalIgnoreCase) ||
                                                item.Current.Name.Contains("clipboard", StringComparison.OrdinalIgnoreCase));
    if (copyItem is not null && copyItem.TryGetCurrentPattern(InvokePattern.Pattern, out var copyPattern))
    {
        var sequenceBefore = GetClipboardSequenceNumber();
        ((InvokePattern)copyPattern).Invoke();
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && GetClipboardSequenceNumber() == sequenceBefore)
            Thread.Sleep(25);
        clipboardText = ReadClipboardText();
        copyInvoked = true;
    }

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        available = true,
        steps,
        copyInvoked,
        clipboardLength = clipboardText?.Length ?? 0,
        clipboardBlockWriteCount = clipboardText?.Split("Block Write", StringSplitOptions.None).Length - 1 ?? 0,
        items = items.Select(ToMenuItem).ToArray()
    }, new JsonSerializerOptions { WriteIndented = true }));
    SendEscape();
}

static void ProbeCaptureMouseContextMenu()
{
    var capture = FindCaptureWindow();
    if (capture is null)
    {
        Console.WriteLine("{\"available\":false,\"reason\":\"captureWindow not found\"}");
        return;
    }

    var grid = capture.FindFirst(
        TreeScope.Descendants,
        new PropertyCondition(AutomationElement.AutomationIdProperty, "treeViewAdv1")) ?? capture;
    var bounds = grid.Current.BoundingRectangle;
    var x = (int)Math.Round(bounds.X + Math.Min(80, Math.Max(10, bounds.Width / 4)));
    var y = (int)Math.Round(bounds.Y + Math.Min(42, Math.Max(30, bounds.Height / 4)));
    var sigmaWindow = FindSigmaWindow();
    var foregroundSet = sigmaWindow is not null && SetForegroundWindow(sigmaWindow.Current.NativeWindowHandle);
    SetCursorPos(x, y);
    MouseEvent(0x0002); // left down
    MouseEvent(0x0004); // left up
    Thread.Sleep(100);
    SendMessage((IntPtr)grid.Current.NativeWindowHandle, 0x007B, (IntPtr)grid.Current.NativeWindowHandle, new IntPtr(-1));
    var selectedMenuItems = WaitForMenuItems(TimeSpan.FromSeconds(1));
    if (selectedMenuItems.Length > 0)
    {
        var selectedCopy = selectedMenuItems.FirstOrDefault(item => item.Current.Name.Contains("copy", StringComparison.OrdinalIgnoreCase) ||
                                                                      item.Current.Name.Contains("clipboard", StringComparison.OrdinalIgnoreCase));
        if (selectedCopy is not null && selectedCopy.TryGetCurrentPattern(InvokePattern.Pattern, out var selectedPattern))
            ((InvokePattern)selectedPattern).Invoke();
    }
    SendEscape();
    MouseEvent(0x0008); // right down
    MouseEvent(0x0010); // right up
    var items = WaitForMenuItems(TimeSpan.FromSeconds(2));
    if (items.Length == 0)
    {
        var packedPoint = new IntPtr((y << 16) | (x & 0xFFFF));
        SendMessage((IntPtr)grid.Current.NativeWindowHandle, 0x007B, (IntPtr)grid.Current.NativeWindowHandle, packedPoint);
        items = WaitForMenuItems(TimeSpan.FromSeconds(2));
    }
    var copyItem = items.FirstOrDefault(item => item.Current.Name.Contains("copy", StringComparison.OrdinalIgnoreCase) ||
                                                item.Current.Name.Contains("clipboard", StringComparison.OrdinalIgnoreCase));
    var copyInvoked = false;
    string? clipboardText = null;
    if (copyItem is not null && copyItem.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
    {
        var sequenceBefore = GetClipboardSequenceNumber();
        ((InvokePattern)pattern).Invoke();
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && GetClipboardSequenceNumber() == sequenceBefore)
            Thread.Sleep(25);
        clipboardText = ReadClipboardText();
        copyInvoked = true;
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        available = true,
        point = new { x, y },
        nativeWindowHandle = grid.Current.NativeWindowHandle,
        gridBounds = bounds.ToString(),
        selectedContextMenuItems = selectedMenuItems.Select(ToMenuItem).ToArray(),
        copyInvoked,
        clipboardText,
        items = items.Select(ToMenuItem).ToArray()
    }, new JsonSerializerOptions { WriteIndented = true }));
    SendEscape();
}

static void ProbeCaptureSelectedContextMenu()
{
    var capture = FindCaptureWindow();
    if (capture is null)
    {
        Console.WriteLine("{\"available\":false,\"reason\":\"captureWindow not found\"}");
        return;
    }

    var grid = capture.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "treeViewAdv1")) ?? capture;
    grid.SetFocus();
    SendMessage((IntPtr)grid.Current.NativeWindowHandle, 0x007B, (IntPtr)grid.Current.NativeWindowHandle, new IntPtr(-1));
    var items = WaitForMenuItems(TimeSpan.FromSeconds(2));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        available = true,
        nativeWindowHandle = grid.Current.NativeWindowHandle,
        items = items.Select(ToMenuItem).ToArray()
    }, new JsonSerializerOptions { WriteIndented = true }));
    SendEscape();
}

static void ProbeCaptureSelection()
{
    var capture = FindCaptureWindow();
    if (capture is null)
    {
        Console.WriteLine("{\"available\":false,\"reason\":\"captureWindow not found\"}");
        return;
    }

    var grid = capture.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "treeViewAdv1")) ?? capture;
    var sigmaWindow = FindSigmaWindow();
    var foregroundSet = sigmaWindow is not null && SetForegroundWindow(sigmaWindow.Current.NativeWindowHandle);
    grid.SetFocus();
    var bounds = grid.Current.BoundingRectangle;
    var hasClickablePoint = grid.TryGetClickablePoint(out var clickablePoint);
    var rowX = hasClickablePoint ? (int)Math.Round(clickablePoint.X) : (int)Math.Round(bounds.X + Math.Min(80, Math.Max(10, bounds.Width / 4)));
    var rowY = hasClickablePoint ? (int)Math.Round(clickablePoint.Y) : (int)Math.Round(bounds.Y + Math.Min(42, Math.Max(30, bounds.Height / 4)));
    SetCursorPos(rowX, rowY);
    MouseEvent(0x0002); // left down
    MouseEvent(0x0004); // left up
    Thread.Sleep(100);
    SendNavigationKey(0x24, 0x47); // Home
    SendShiftEnd();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        available = true,
        foregroundSet,
        foregroundTitle = GetForegroundWindowTitle(),
        hasFocus = grid.Current.HasKeyboardFocus,
        nativeWindowHandle = grid.Current.NativeWindowHandle,
        hasClickablePoint,
        bounds = bounds.ToString(),
        clickPoint = new { x = rowX, y = rowY }
    }));
    Thread.Sleep(10000);
}

static void ProbeCaptureKeyOnly()
{
    var capture = FindCaptureWindow();
    if (capture is null)
    {
        Console.WriteLine("{\"available\":false,\"reason\":\"captureWindow not found\"}");
        return;
    }

    var grid = capture.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "treeViewAdv1")) ?? capture;
    var sigmaWindow = FindSigmaWindow();
    var foregroundSet = sigmaWindow is not null && SetForegroundWindow(sigmaWindow.Current.NativeWindowHandle);
    grid.SetFocus();
    SendNavigationKey(0x24, 0x47);
    SendShiftEnd();
    SendMessage((IntPtr)grid.Current.NativeWindowHandle, 0x007B, (IntPtr)grid.Current.NativeWindowHandle, new IntPtr(-1));
    var items = WaitForMenuItems(TimeSpan.FromSeconds(2));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        available = true,
        foregroundSet,
        foregroundTitle = GetForegroundWindowTitle(),
        items = items.Select(ToMenuItem).ToArray()
    }, new JsonSerializerOptions { WriteIndented = true }));
    SendEscape();
}

static AutomationElement[] WaitForMenuItems(TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    AutomationElement[] items = [];
    while (DateTime.UtcNow < deadline)
    {
        items = AutomationElement.RootElement
            .FindAll(TreeScope.Subtree, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Where(element => element.Current.ControlType == ControlType.MenuItem &&
                              !string.IsNullOrWhiteSpace(element.Current.Name))
            .Where(element => element.Current.Name.Contains("copy", StringComparison.OrdinalIgnoreCase) ||
                              element.Current.Name.Contains("clipboard", StringComparison.OrdinalIgnoreCase) ||
                              element.Current.Name.Contains("másol", StringComparison.OrdinalIgnoreCase) ||
                              element.Current.Name.Contains("clear", StringComparison.OrdinalIgnoreCase) ||
                              element.Current.Name.Contains("sequence", StringComparison.OrdinalIgnoreCase) ||
                              element.Current.Name.Contains("raw", StringComparison.OrdinalIgnoreCase) ||
                              element.Current.Name.Contains("text", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (items.Length > 0) break;
        Thread.Sleep(25);
    }
    return items;
}

static object ToMenuItem(AutomationElement element) => new
{
    name = element.Current.Name,
    automationId = element.Current.AutomationId,
    className = element.Current.ClassName,
    controlType = element.Current.ControlType.ProgrammaticName,
    isEnabled = element.Current.IsEnabled,
    patterns = element.GetSupportedPatterns().Select(pattern => pattern.ProgrammaticName).ToArray()
};

static void SendShiftEnd()
{
    KeybdEvent(0x10, 0);
    Thread.Sleep(50);
    KeybdEventExtended(0x23, 0x4F, 0x0001, UIntPtr.Zero);
    Thread.Sleep(50);
    KeybdEventExtended(0x23, 0x4F, 0x0003, UIntPtr.Zero);
    KeybdEvent(0x10, 2);
}

static void SendNavigationKey(byte key, byte scanCode)
{
    KeybdEventExtended(key, scanCode, 0x0001, UIntPtr.Zero);
    Thread.Sleep(50);
    KeybdEventExtended(key, scanCode, 0x0003, UIntPtr.Zero);
}

static string? ReadClipboardText()
{
    string? text = null;
    var thread = new Thread(() =>
    {
        try { text = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null; }
        catch { }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join(TimeSpan.FromSeconds(2));
    return text;
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

static void KeybdEventExtended(byte key, byte scanCode, uint flags, UIntPtr extraInfo) => keybd_event(key, scanCode, flags, extraInfo);

static void MouseEvent(uint flags) => mouse_event(flags, 0, 0, 0, UIntPtr.Zero);

static uint GetClipboardSequenceNumber() => GetClipboardSequenceNumberNative();

static bool SetForegroundWindow(int nativeWindowHandle) => SetForegroundWindowNative((IntPtr)nativeWindowHandle);

static string? GetForegroundWindowTitle()
{
    var handle = GetForegroundWindowNative();
    if (handle == IntPtr.Zero) return null;
    var builder = new System.Text.StringBuilder(256);
    return GetWindowTextNative(handle, builder, builder.Capacity) > 0 ? builder.ToString() : null;
}

[DllImport("user32.dll", EntryPoint = "GetClipboardSequenceNumber")]
static extern uint GetClipboardSequenceNumberNative();

[DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool SetForegroundWindowNative(IntPtr windowHandle);

[DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
static extern IntPtr GetForegroundWindowNative();

[DllImport("user32.dll", EntryPoint = "GetWindowText")]
static extern int GetWindowTextNative(IntPtr windowHandle, System.Text.StringBuilder text, int maxCount);

[DllImport("user32.dll", EntryPoint = "SendMessage")]
static extern IntPtr SendMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

[DllImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool SetCursorPos(int x, int y);

[DllImport("user32.dll")]
static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

[DllImport("user32.dll", SetLastError = true)]
static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
