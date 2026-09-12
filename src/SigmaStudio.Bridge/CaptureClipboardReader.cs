using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Runtime.InteropServices;

namespace SigmaStudio.Bridge;

public sealed class CaptureClipboardReader
{
    private readonly TimeSpan _timeout;

    public CaptureClipboardReader(TimeSpan? timeout = null) => _timeout = timeout ?? TimeSpan.FromSeconds(3);

    public bool TryRead(Func<bool> invokeCopy, out string? text, out string? error)
    {
        text = null;
        error = null;
        IDataObject? previous = null;
        try
        {
            previous = Clipboard.GetDataObject();
            var beforeSequence = GetClipboardSequenceNumber();
            if (!invokeCopy())
            {
                error = "CAPTURE_COPY_ACTION_UNAVAILABLE";
                return false;
            }

            var deadline = Stopwatch.GetTimestamp() + (long)(_timeout.TotalSeconds * Stopwatch.Frequency);
            while (GetClipboardSequenceNumber() == beforeSequence && Stopwatch.GetTimestamp() < deadline)
                Thread.Sleep(25);
            if (GetClipboardSequenceNumber() == beforeSequence)
            {
                error = "CAPTURE_CLIPBOARD_TIMEOUT";
                return false;
            }

            if (!Clipboard.ContainsText())
            {
                error = "CAPTURE_CLIPBOARD_NOT_TEXT";
                return false;
            }
            text = Clipboard.GetText(TextDataFormat.UnicodeText);
            return !string.IsNullOrWhiteSpace(text);
        }
        catch (ExternalException ex)
        {
            error = $"CAPTURE_CLIPBOARD_ERROR: {ex.Message}";
            return false;
        }
        catch (InvalidOperationException ex)
        {
            error = $"CAPTURE_CLIPBOARD_ERROR: {ex.Message}";
            return false;
        }
        finally
        {
            try
            {
                if (previous is null) Clipboard.Clear();
                else Clipboard.SetDataObject(previous, true);
            }
            catch
            {
                // Clipboard restoration is best effort and must not hide the read result.
            }
        }
    }

    private static uint GetClipboardSequenceNumber() => GetClipboardSequenceNumberNative();

    [DllImport("user32.dll", EntryPoint = "GetClipboardSequenceNumber")]
    private static extern uint GetClipboardSequenceNumberNative();
}
