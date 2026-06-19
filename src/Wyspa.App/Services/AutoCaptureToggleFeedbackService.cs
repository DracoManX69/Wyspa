using System.IO;
using System.Media;
using System.Windows;

namespace Wyspa.App.Services;

public sealed class AutoCaptureToggleFeedbackService
{
    private static readonly Uri OnToneUri = new("pack://application:,,,/Assets/AutoCaptureOn.wav", UriKind.Absolute);
    private static readonly Uri OffToneUri = new("pack://application:,,,/Assets/AutoCaptureOff.wav", UriKind.Absolute);
    private readonly OverlayStatusService _overlay;

    public AutoCaptureToggleFeedbackService(OverlayStatusService overlay)
    {
        _overlay = overlay;
    }

    public void Show(bool isListening, double overlayOpacity)
    {
        _overlay.SetOpacity(overlayOpacity);
        _overlay.ShowAutoCaptureToggle(isListening);
        PlayTone(isListening);
    }

    private static void PlayTone(bool isListening)
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(isListening ? OnToneUri : OffToneUri);
            if (resource?.Stream is null)
            {
                SystemSounds.Asterisk.Play();
                return;
            }

            using var tone = new MemoryStream();
            resource.Stream.CopyTo(tone);
            var toneBytes = tone.ToArray();
            _ = Task.Run(() =>
            {
                using var stream = new MemoryStream(toneBytes);
                using var player = new SoundPlayer(stream);
                player.PlaySync();
            });
        }
        catch
        {
            SystemSounds.Asterisk.Play();
        }
    }
}
