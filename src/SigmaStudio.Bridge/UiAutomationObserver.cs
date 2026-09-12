using System.Windows.Automation;

namespace SigmaStudio.Bridge;

public sealed class UiAutomationObserver
{
    public bool IsAvailable => OperatingSystem.IsWindows();

    public object ReadStatus()
    {
        if (!IsAvailable) return new { available = false, reason = "Windows UI Automation is unavailable." };
        var root = AutomationElement.RootElement;
        var windows = root.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
        var sigma = windows.Cast<AutomationElement>().FirstOrDefault(w => (w.Current.Name ?? "").Contains("SigmaStudio", StringComparison.OrdinalIgnoreCase));
        return new { available = sigma is not null, window = sigma?.Current.Name, status = FindStatusText(sigma) };
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
        return statusBars.Cast<AutomationElement>().Select(s => s.Current.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
    }
}
