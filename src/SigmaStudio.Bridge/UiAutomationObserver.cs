using System.IO;
using System.Diagnostics;
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
            selector = "AutomationId=captureWindow; rows=DataItem/ListItem/TreeItem/Custom; clipboard=verified-copy-action-only",
            copyAction = DiscoverCaptureCopyAction() ?? "not-exposed-by-local-build",
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
        if (DiscoverCaptureCopyAction() is null)
        {
            warning = "CAPTURE_COPY_ACTION_UNAVAILABLE: the local SigmaStudio 4.7 capture control exposes no verified Copy/Copy All InvokePattern, and the keyboard shortcut probe did not produce clipboard text.";
            return false;
        }

        if (!_clipboardReader.TryRead(() => TryInvokeCaptureCopy(captureWindow), out var text, out var error))
        {
            warning = error ?? "CAPTURE_CLIPBOARD_READ_FAILED";
            return false;
        }

        rows = CaptureTextParser.Parse(text ?? string.Empty)
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
        warning = string.Empty;
        return true;
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

    private string? DiscoverCaptureCopyAction()
    {
        var captureWindow = FindCaptureWindow();
        var toolbar = captureWindow?.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "CaptureWndtoolStrip"));
        var candidate = toolbar?.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>()
            .FirstOrDefault(element => IsCopyAction(element) && element.TryGetCurrentPattern(InvokePattern.Pattern, out _));
        return candidate is null ? null : candidate.Current.AutomationId ?? candidate.Current.Name;
    }

    private static bool TryInvokeCaptureCopy(AutomationElement captureWindow)
    {
        var toolbar = captureWindow.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "CaptureWndtoolStrip"));
        var candidate = toolbar?.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>()
            .FirstOrDefault(element => IsCopyAction(element) && element.TryGetCurrentPattern(InvokePattern.Pattern, out _));
        if (candidate is null) return false;
        try
        {
            ((InvokePattern)candidate.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
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

    private sealed record ObservedCaptureRow(
        string Identity,
        string Direction,
        string Summary,
        string? Block,
        string? Control,
        JsonElement? Payload);
}
