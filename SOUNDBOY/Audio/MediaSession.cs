using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace SOUNDBOY.Audio;

/// <summary>
/// Registers SOUNDBOY with Windows' System Media Transport Controls, which is how hardware media keys
/// (Play/Pause, Next, Previous, Stop, Rewind, Fast-forward), Bluetooth headset buttons and the
/// Windows volume flyout reach the player. A muted, idle MediaPlayer instance is used only as the
/// SMTC host (the standard approach for unpackaged desktop apps); audio still plays through NAudio.
/// Events are raised on a background thread.
/// </summary>
public sealed class MediaSession : IDisposable
{
    readonly MediaPlayer host;
    readonly SystemMediaTransportControls smtc;

    public event Action<SystemMediaTransportControlsButton>? ButtonPressed;
    public event Action<TimeSpan>? SeekRequested;

    public MediaSession()
    {
        host = new MediaPlayer();
        host.CommandManager.IsEnabled = false;
        smtc = host.SystemMediaTransportControls;
        smtc.IsEnabled = true;
        smtc.IsPlayEnabled = true;
        smtc.IsPauseEnabled = true;
        smtc.IsStopEnabled = true;
        smtc.IsNextEnabled = true;
        smtc.IsPreviousEnabled = true;
        smtc.IsRewindEnabled = true;
        smtc.IsFastForwardEnabled = true;
        smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
        smtc.ButtonPressed += (_, e) => ButtonPressed?.Invoke(e.Button);
        smtc.PlaybackPositionChangeRequested += (_, e) => SeekRequested?.Invoke(e.RequestedPlaybackPosition);
    }

    public void SetState(PlayerState state) => smtc.PlaybackStatus = state switch
    {
        PlayerState.Playing => MediaPlaybackStatus.Playing,
        PlayerState.Paused => MediaPlaybackStatus.Paused,
        _ => MediaPlaybackStatus.Stopped,
    };

    public async void SetTrack(string title, string? artist, string? album, byte[]? thumbnailPng)
    {
        try
        {
            var u = smtc.DisplayUpdater;
            u.ClearAll();
            u.Type = MediaPlaybackType.Music;
            u.MusicProperties.Title = title;
            u.MusicProperties.Artist = artist ?? "";
            u.MusicProperties.AlbumTitle = album ?? "";
            if (thumbnailPng is { Length: > 0 })
            {
                var stream = new InMemoryRandomAccessStream();
                using (var w = new DataWriter(stream))
                {
                    w.WriteBytes(thumbnailPng);
                    await w.StoreAsync();
                    w.DetachStream();
                }
                stream.Seek(0);
                u.Thumbnail = RandomAccessStreamReference.CreateFromStream(stream);
            }
            u.Update();
        }
        catch
        {
            // The flyout metadata is cosmetic; never let it break playback.
        }
    }

    public void SetTimeline(TimeSpan position, TimeSpan duration)
    {
        try
        {
            smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero,
                MinSeekTime = TimeSpan.Zero,
                Position = position,
                MaxSeekTime = duration,
                EndTime = duration,
            });
        }
        catch { }
    }

    public void Dispose()
    {
        try
        {
            smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
            smtc.DisplayUpdater.ClearAll();
            smtc.DisplayUpdater.Update();
        }
        catch { }
        host.Dispose();
    }
}
