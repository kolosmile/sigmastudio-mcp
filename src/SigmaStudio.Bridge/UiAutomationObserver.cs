using System.IO;
using System.Windows.Automation;
using SigmaStudio.Contracts;

namespace SigmaStudio.Bridge;

public sealed class UiAutomationObserver
{
    private readonly object _captureGate = new();
    private readonly List<CaptureEntryDto> _capture = [];
    private readonly HashSet<string> _captureKeys = new(StringComparer.Ordinal);
    private long _captureSequence;

    public bool IsAvailable => Environment.OSVersion.Platform == PlatformID.Win32NT;

    public object ReadStatus()
    {
        if (!IsAvailable) return new { available = false, reason = "Windows UI Automation is unavailable." };
        var root = AutomationElement.RootElement;
        var windows = root.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
        var sigma = windows.Cast<AutomationElement>().FirstOrDefault(w => (w.Current.Name ?? "").IndexOf("SigmaStudio", StringComparison.OrdinalIgnoreCase) >= 0);
        return new { available = sigma is not null, window = sigma?.Current.Name, status = FindStatusText(sigma), statusControl = FindStatusControl(sigma) };
    }

    public object ReadCapture()
    {
        if (!IsAvailable) return new { available = false, entries = Array.Empty<CaptureEntryDto>(), reason = "Capture selector profile is unavailable outside Windows." };
        var entries = ReadCaptureEntries();
        return new { available = FindCaptureWindow() is not null, selector = "AutomationId=captureWindow; rows=DataItem/ListItem/Custom", entries };
    }

    public IReadOnlyList<CaptureEntryDto> ReadCaptureEntries()
    {
        if (!IsAvailable) return Array.Empty<CaptureEntryDto>();
        var captureWindow = FindCaptureWindow();
        if (captureWindow is null) lock (_captureGate) return _capture.ToArray();

        var candidates = captureWindow.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>()
            .Where(IsCaptureRow)
            .Select(ReadElementText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        lock (_captureGate)
        {
            foreach (var summary in candidates)
            {
                var key = $"capture|{summary}";
                if (!_captureKeys.Add(key)) continue;
                var direction = summary.IndexOf("write", StringComparison.OrdinalIgnoreCase) >= 0 ? "write" :
                    summary.IndexOf("read", StringComparison.OrdinalIgnoreCase) >= 0 ? "read" : "observation";
                _capture.Add(new CaptureEntryDto(++_captureSequence, DateTimeOffset.UtcNow, direction, "CAPTURE", summary));
            }
            if (_capture.Count > 2000) _capture.RemoveRange(0, _capture.Count - 2000);
            return _capture.ToArray();
        }
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
        return windows.Cast<AutomationElement>().FirstOrDefault(w => (w.Current.Name ?? "").IndexOf("SigmaStudio", StringComparison.OrdinalIgnoreCase) >= 0);
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
}
