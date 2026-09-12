using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using SigmaStudio.Contracts;

namespace SigmaStudio.Bridge;

public sealed class UiAutomationObserver
{
    private readonly object _captureGate = new();
    private readonly List<CaptureEntryDto> _capture = [];
    private readonly CaptureClipboardReader _clipboardReader = new();
    private IReadOnlyList<ObservedCaptureRow> _lastCaptureRows = [];
    private string? _captureWarning;
    private string? _captureCopyAction;
    private string? _captureCopyDiagnostic;
    private int _captureCopyAttempt;
    private long _captureSequence;

    public bool IsAvailable => Environment.OSVersion.Platform == PlatformID.Win32NT;
    public string? CaptureWarning
    {
        get { lock (_captureGate) return _captureWarning; }
    }

    public object ReadStatus()
    {
        return ReadStatusObservation();
    }

    public UiStateObservationDto ReadStatusObservation()
    {
        if (!IsAvailable) return new UiStateObservationDto(false, null, null, null, null);
        var root = AutomationElement.RootElement;
        var windows = root.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
        var candidates = windows.Cast<AutomationElement>()
            .Where(w => (w.Current.Name ?? "").IndexOf("SigmaStudio", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();
        var sigma = candidates.FirstOrDefault(IsSigmaStudioProcess);
        var rawText = FindStatusText(sigma);
        return new UiStateObservationDto(
            sigma is not null,
            sigma?.Current.Name,
            rawText,
            FindStatusControl(sigma),
            UiStateNormalizer.Normalize(rawText));
    }

    public object ReadCapture()
    {
        if (!IsAvailable) return new { available = false, entries = Array.Empty<CaptureEntryDto>(), reason = "Capture selector profile is unavailable outside Windows." };
        var entries = ReadCaptureEntries();
        return new
        {
            available = FindCaptureWindow() is not null,
            selector = "AutomationId=captureWindow; grid=treeViewAdv1; clipboard=Home+Shift+End+selected-row-Copy-to-clipboard",
            copyAction = _captureCopyAction ?? DiscoverToolbarCopyAction() ?? "selected-row-context-menu:Copy to clipboard",
            warning = _captureWarning,
            entries
        };
    }

    public IReadOnlyList<CaptureEntryDto> ReadCaptureEntries()
    {
        if (!IsAvailable) return Array.Empty<CaptureEntryDto>();
        var captureWindow = FindCaptureWindow();
        if (captureWindow is null)
        {
            lock (_captureGate) return _capture.ToArray();
        }

        var rows = ReadUiAutomationRows(captureWindow);
        if (rows.Count == 0)
        {
            if (TryReadClipboardRows(captureWindow, out var clipboardRows, out var warning))
            {
                rows = clipboardRows;
                _captureWarning = null;
            }
            else
            {
                _captureWarning = warning;
                lock (_captureGate) return _capture.ToArray();
            }
        }
        else
        {
            _captureWarning = null;
        }

        return AppendCaptureRows(rows);
    }

    private IReadOnlyList<ObservedCaptureRow> ReadUiAutomationRows(AutomationElement captureWindow)
    {
        return captureWindow.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>()
            .Where(IsCaptureRow)
            .Select(ReadElementText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(summary => new ObservedCaptureRow(
                summary,
                summary.IndexOf("write", StringComparison.OrdinalIgnoreCase) >= 0 ? "write" :
                    summary.IndexOf("read", StringComparison.OrdinalIgnoreCase) >= 0 ? "read" : "observation",
                summary,
                null,
                null,
                JsonSerializer.SerializeToElement(new { rawText = summary }).Clone()))
            .ToArray();
    }

    private bool TryReadClipboardRows(AutomationElement captureWindow, out IReadOnlyList<ObservedCaptureRow> rows, out string warning)
    {
        rows = [];
        string? lastError = null;
        for (_captureCopyAttempt = 0; _captureCopyAttempt < 3; _captureCopyAttempt++)
        {
            if (!_clipboardReader.TryRead(() => TryInvokeCaptureCopy(captureWindow), out var text, out var error))
            {
                lastError = error;
                continue;
            }

            rows = ParseClipboardRows(text ?? string.Empty);
            if (rows.Count > 0)
            {
                warning = string.Empty;
                return true;
            }
            lastError = "CAPTURE_CLIPBOARD_EMPTY";
        }

        warning = lastError == "CAPTURE_COPY_ACTION_UNAVAILABLE"
            ? $"CAPTURE_COPY_ACTION_UNAVAILABLE: the local SigmaStudio 4.7 Capture grid did not expose the verified selected-range Copy to clipboard action. {_captureCopyDiagnostic}"
            : lastError ?? "CAPTURE_CLIPBOARD_READ_FAILED";
        return false;
    }

    private static IReadOnlyList<ObservedCaptureRow> ParseClipboardRows(string text)
    {
        return CaptureTextParser.Parse(text)
            .Select(row => new ObservedCaptureRow(
                row.RawText,
                row.Direction,
                string.IsNullOrWhiteSpace(row.Summary) ? row.RawText : row.Summary,
                row.CellName,
                row.ParameterName,
                JsonSerializer.SerializeToElement(new
                {
                    mode = row.Mode,
                    cellName = row.CellName,
                    parameterName = row.ParameterName,
                    address = row.Address,
                    value = row.NumericValue ?? (object?)row.ValueText,
                    data = row.Data,
                    bytes = row.ByteCount,
                    time = row.Time,
                    sender = row.Sender,
                    rawColumns = row.RawColumns,
                    rawText = row.RawText
                }).Clone()))
            .ToArray();
    }

    private IReadOnlyList<CaptureEntryDto> AppendCaptureRows(IReadOnlyList<ObservedCaptureRow> rows)
    {
        lock (_captureGate)
        {
            var previous = _lastCaptureRows;
            var appendFrom = CaptureSnapshotDelta.AppendStart(previous.Select(row => row.Identity).ToArray(), rows.Select(row => row.Identity).ToArray());
            _lastCaptureRows = rows.ToArray();
            for (var index = appendFrom; index < rows.Count; index++)
            {
                var row = rows[index];
                _capture.Add(new CaptureEntryDto(++_captureSequence, DateTimeOffset.UtcNow, row.Direction, "CAPTURE", row.Summary, row.Block, row.Control, row.Payload));
            }
            if (_capture.Count > 2000) _capture.RemoveRange(0, _capture.Count - 2000);
            return _capture.ToArray();
        }
    }

    private string? DiscoverToolbarCopyAction()
    {
        var captureWindow = FindCaptureWindow();
        if (captureWindow is null) return null;
        var toolbar = captureWindow.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "CaptureWndtoolStrip"));
        var candidate = toolbar?.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>()
            .FirstOrDefault(element => IsCopyAction(element) && element.TryGetCurrentPattern(InvokePattern.Pattern, out _));
        return candidate is null ? null : candidate.Current.AutomationId ?? candidate.Current.Name;
    }

    private bool TryInvokeCaptureCopy(AutomationElement captureWindow)
    {
        _captureCopyDiagnostic = null;
        var toolbar = captureWindow.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "CaptureWndtoolStrip"));
        var candidate = toolbar?.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>()
            .FirstOrDefault(element => IsCopyAction(element) && element.TryGetCurrentPattern(InvokePattern.Pattern, out _));
        var contextMenuAction = false;
        var previousForeground = IntPtr.Zero;
        if (candidate is null)
        {
            candidate = OpenSelectedRowCopyAction(captureWindow, out previousForeground);
            if (candidate is null) return false;
            contextMenuAction = true;
        }
        try
        {
            if (contextMenuAction && _captureCopyAttempt == 0)
            {
                // SigmaStudio opens this owner-drawn context menu asynchronously.  The
                // menu item has to be visible before keyboard navigation starts; otherwise
                // Down is delivered to the capture grid and Enter does nothing.
                SendCopyMenuKeySequence();
            }
            else if (contextMenuAction)
            {
                // If SigmaStudio left a previous menu item selected, a second Down can
                // advance past Copy.  Once the first keyboard attempt was rejected by
                // the clipboard format, invoke the verified Copy item directly.
                ((InvokePattern)candidate.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            }
            else
            {
                ((InvokePattern)candidate.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            }
            _captureCopyAction = contextMenuAction
                ? candidate.Current.AutomationId is { Length: > 0 } automationId
                    ? $"context-menu:{candidate.Current.Name} ({automationId}); selected-range"
                    : $"context-menu:{candidate.Current.Name}; selected-range"
                : $"toolbar:{candidate.Current.AutomationId ?? candidate.Current.Name}";
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        finally
        {
            if (contextMenuAction && previousForeground != IntPtr.Zero)
                SetForegroundWindowNative(previousForeground);
        }
    }

    private AutomationElement? OpenSelectedRowCopyAction(AutomationElement captureWindow, out IntPtr previousForeground)
    {
        previousForeground = IntPtr.Zero;
        var grid = captureWindow.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "treeViewAdv1"));
        if (grid is null)
        {
            _captureCopyDiagnostic = "CAPTURE_GRID_NOT_FOUND";
            return null;
        }
        if (grid.Current.NativeWindowHandle == 0)
        {
            _captureCopyDiagnostic = "CAPTURE_GRID_NATIVE_HANDLE_MISSING";
            return null;
        }

        var clickX = 0;
        var clickY = 0;
        try
        {
            var sigmaWindow = FindSigmaWindow();
            if (sigmaWindow is null)
            {
                _captureCopyDiagnostic = "SIGMASTUDIO_WINDOW_NOT_FOUND";
                return null;
            }
            if (!grid.TryGetClickablePoint(out var clickablePoint))
            {
                _captureCopyDiagnostic = "CAPTURE_GRID_CLICKABLE_POINT_UNAVAILABLE";
                return null;
            }
            previousForeground = GetForegroundWindowNative();
            var foregroundSet = SetForegroundWindowNative((IntPtr)sigmaWindow.Current.NativeWindowHandle);
            CloseCaptureContextMenu();
            grid.SetFocus();
            var gridBounds = grid.Current.BoundingRectangle;
            clickX = (int)Math.Round(gridBounds.X + Math.Min(80, Math.Max(10, gridBounds.Width / 4)));
            clickY = (int)Math.Round(clickablePoint.Y);
            SetCursorPosNative(clickX, clickY);
            MouseEventNative(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
            MouseEventNative(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(100);
            SendNavigationKey(VkHome, ScanHome);
            SendShiftEnd();
            SetCursorPosNative(clickX, clickY);
            MouseEventNative(MouseRightDown, 0, 0, 0, UIntPtr.Zero);
            MouseEventNative(MouseRightUp, 0, 0, 0, UIntPtr.Zero);
            // The menu is posted by SigmaStudio's UI thread, not opened synchronously
            // by the mouse message.  Give it time to materialize before looking it up or
            // sending the user-confirmed Down+Enter sequence.
            Thread.Sleep(125);
            _captureCopyDiagnostic = $"foregroundSet={foregroundSet};point={clickX},{clickY}";
        }
        catch (ElementNotAvailableException)
        {
            _captureCopyDiagnostic = "CAPTURE_UI_ELEMENT_NOT_AVAILABLE";
            RestoreForeground(previousForeground);
            return null;
        }

        var item = FindCopyMenuItem(captureWindow, TimeSpan.FromSeconds(2));
        if (item is null)
        {
            var packedPoint = new IntPtr(unchecked((int)((clickX & 0xffff) | ((clickY & 0xffff) << 16))));
            SendMessage((IntPtr)grid.Current.NativeWindowHandle, WmContextMenu, (IntPtr)grid.Current.NativeWindowHandle, packedPoint);
            item = FindCopyMenuItem(captureWindow, TimeSpan.FromSeconds(2));
        }
        if (item is not null) return item;

        CloseCaptureContextMenu();
        _captureCopyDiagnostic = (_captureCopyDiagnostic ?? "") + ";CAPTURE_COPY_MENU_NOT_FOUND";
        RestoreForeground(previousForeground);
        previousForeground = IntPtr.Zero;
        return null;
    }

    private static AutomationElement? FindCopyMenuItem(AutomationElement captureWindow, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var item = AutomationElement.RootElement
                .FindAll(TreeScope.Subtree, Condition.TrueCondition)
                .Cast<AutomationElement>()
                .FirstOrDefault(element => element.Current.ProcessId == captureWindow.Current.ProcessId &&
                                           element.Current.ControlType == ControlType.MenuItem &&
                                           !element.Current.IsOffscreen &&
                                           IsCopyAction(element) &&
                                           element.TryGetCurrentPattern(InvokePattern.Pattern, out _));
            if (item is not null) return item;
            Thread.Sleep(25);
        }
        return null;
    }

    private static void CloseCaptureContextMenu()
    {
        KeybdEvent(VkEscape, 0, 0, UIntPtr.Zero);
        KeybdEvent(VkEscape, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private static void RestoreForeground(IntPtr windowHandle)
    {
        if (windowHandle != IntPtr.Zero) SetForegroundWindowNative(windowHandle);
    }

    private static void SendNavigationKey(byte key, byte scanCode)
    {
        KeybdEvent(key, scanCode, KeyEventExtended, UIntPtr.Zero);
        Thread.Sleep(50);
        KeybdEvent(key, scanCode, KeyEventExtended | KeyEventKeyUp, UIntPtr.Zero);
    }

    private static void SendShiftEnd()
    {
        KeybdEvent(VkShift, 0, 0, UIntPtr.Zero);
        Thread.Sleep(50);
        KeybdEvent(VkEnd, ScanEnd, KeyEventExtended, UIntPtr.Zero);
        Thread.Sleep(50);
        KeybdEvent(VkEnd, ScanEnd, KeyEventExtended | KeyEventKeyUp, UIntPtr.Zero);
        KeybdEvent(VkShift, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private static void SendCopyMenuKeySequence()
    {
        SendNavigationKey(VkDown, ScanDown);
        Thread.Sleep(50);
        KeybdEvent(VkEnter, ScanEnter, 0, UIntPtr.Zero);
        Thread.Sleep(50);
        KeybdEvent(VkEnter, ScanEnter, KeyEventKeyUp, UIntPtr.Zero);
    }

    private static bool IsCopyAction(AutomationElement element)
    {
        var text = $"{element.Current.Name} {element.Current.AutomationId} {element.Current.HelpText}";
        return text.IndexOf("copy", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("clipboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("másol", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static AutomationElement? FindCaptureWindow()
    {
        var sigma = FindSigmaWindow();
        if (sigma is null) return null;
        return sigma.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "captureWindow"));
    }

    private static AutomationElement? FindSigmaWindow()
    {
        var root = AutomationElement.RootElement;
        var windows = root.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
        var candidates = windows.Cast<AutomationElement>()
            .Where(w => (w.Current.Name ?? "").IndexOf("SigmaStudio", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();
        return candidates.FirstOrDefault(IsSigmaStudioProcess) ?? candidates.FirstOrDefault();
    }

    private static bool IsSigmaStudioProcess(AutomationElement element)
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

    private static bool IsCaptureRow(AutomationElement element)
    {
        var type = element.Current.ControlType;
        return type == ControlType.DataItem || type == ControlType.ListItem || type == ControlType.TreeItem || type == ControlType.Custom;
    }

    private static string ReadElementText(AutomationElement element)
    {
        var parts = new List<string>();
        try
        {
            if (!string.IsNullOrWhiteSpace(element.Current.Name)) parts.Add(element.Current.Name);
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern) && !string.IsNullOrWhiteSpace(((ValuePattern)valuePattern).Current.Value))
                parts.Add(((ValuePattern)valuePattern).Current.Value);
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPattern))
            {
                var text = ((TextPattern)textPattern).DocumentRange.GetText(-1).Trim();
                if (!string.IsNullOrWhiteSpace(text)) parts.Add(text);
            }
            foreach (var child in element.FindAll(TreeScope.Children, Condition.TrueCondition).Cast<AutomationElement>())
                if (!string.IsNullOrWhiteSpace(child.Current.Name)) parts.Add(child.Current.Name);
        }
        catch (ElementNotAvailableException)
        {
        }
        return string.Join(" | ", parts.Distinct(StringComparer.Ordinal).Take(32));
    }

    private static string? FindStatusText(AutomationElement? sigma)
    {
        if (sigma is null) return null;
        var statusBars = sigma.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.StatusBar));
        foreach (var statusBar in statusBars.Cast<AutomationElement>())
        {
            var pane = statusBar.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "StatusBar.Pane2"));
            var paneText = pane is null ? null : pane.Current.Name;
            if (!string.IsNullOrWhiteSpace(paneText)) return paneText;
            var current = statusBar.Current;
            var candidates = new List<string?>
            {
                current.HelpText,
                current.ItemStatus,
                ReadValue(statusBar)
            };
            candidates.AddRange(statusBar.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>().Select(child => child.Current.Name));
            var valid = candidates.Where(text => IsStatusText(text, current.AutomationId, sigma.Current.Name)).ToArray();
            var value = valid.FirstOrDefault(text => text!.IndexOf("active", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("download", StringComparison.OrdinalIgnoreCase) >= 0) ?? valid.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private static bool IsStatusText(string? value, string? automationId, string? windowName)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (string.Equals(value, automationId, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(value, windowName, StringComparison.OrdinalIgnoreCase)) return false;
        if (Path.IsPathRooted(value) || value!.EndsWith(".dspproj", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static string? FindStatusControl(AutomationElement? sigma)
    {
        if (sigma is null) return null;
        var statusBar = sigma.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.StatusBar))
            .Cast<AutomationElement>().FirstOrDefault();
        return statusBar?.Current.AutomationId ?? statusBar?.Current.Name;
    }

    private static string? ReadValue(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
                return ((ValuePattern)pattern).Current.Value;
        }
        catch (ElementNotAvailableException)
        {
        }
        return null;
    }

    private const uint WmContextMenu = 0x007B;
    private const byte VkEscape = 0x1B;
    private const byte VkHome = 0x24;
    private const byte VkEnd = 0x23;
    private const byte VkShift = 0x10;
    private const byte VkDown = 0x28;
    private const byte VkEnter = 0x0D;
    private const byte ScanHome = 0x47;
    private const byte ScanEnd = 0x4F;
    private const byte ScanDown = 0x50;
    private const byte ScanEnter = 0x1C;
    private const uint KeyEventExtended = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;

    [DllImport("user32.dll", EntryPoint = "SendMessage")]
    private static extern IntPtr SendMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern IntPtr GetForegroundWindowNative();

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindowNative(IntPtr windowHandle);

    [DllImport("user32.dll", EntryPoint = "SetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPosNative(int x, int y);

    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void MouseEventNative(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll", EntryPoint = "keybd_event")]
    private static extern void KeybdEvent(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    private sealed record ObservedCaptureRow(
        string Identity,
        string Direction,
        string Summary,
        string? Block,
        string? Control,
        JsonElement? Payload);
}
