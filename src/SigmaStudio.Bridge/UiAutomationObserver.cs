using System.IO;
using System.Windows.Automation;

namespace SigmaStudio.Bridge;

public sealed class UiAutomationObserver
{
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
        if (!IsAvailable) return new { available = false, entries = Array.Empty<object>() };
        // Capture Window selectors are intentionally profile-driven and must be verified on
        // the installed SigmaStudio build. OCR/pixel-coordinate automation is not used.
        return new { available = false, entries = Array.Empty<object>(), reason = "Capture selector profile has not been verified." };
    }

    private static string? FindStatusText(AutomationElement? sigma)
    {
        if (sigma is null) return null;
        var statusBars = sigma.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.StatusBar));
        foreach (var statusBar in statusBars.Cast<AutomationElement>())
        {
            var current = statusBar.Current;
            var candidates = new[]
            {
                current.HelpText,
                current.ItemStatus,
                ReadValue(statusBar),
                statusBar.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>().Select(child => child.Current.Name).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
            };
            var value = candidates.FirstOrDefault(text => IsStatusText(text, current.AutomationId, sigma.Current.Name));
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
