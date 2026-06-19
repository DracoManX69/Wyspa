using System.Media;
using System.Windows;
using Wyspa.Core.Models;

namespace Wyspa.App.Services;

public sealed class WakeToneService
{
    private static readonly Uri DefaultToneUri = new("pack://application:,,,/Assets/WakeTone.wav", UriKind.Absolute);

    public void Play(AppSettings settings)
    {
        if (!settings.WakeToneEnabled)
        {
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(settings.WakeTonePath) && System.IO.File.Exists(settings.WakeTonePath))
            {
                using var player = new SoundPlayer(settings.WakeTonePath!);
                player.PlaySync();
                return;
            }

            var resource = System.Windows.Application.GetResourceStream(DefaultToneUri);
            if (resource?.Stream is null)
            {
                SystemSounds.Asterisk.Play();
                return;
            }

            using var defaultPlayer = new SoundPlayer(resource.Stream);
            defaultPlayer.PlaySync();
        }
        catch
        {
            SystemSounds.Asterisk.Play();
        }
    }
}
