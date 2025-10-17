using System;
using System.IO;

namespace CommandEditor.Core.Services;

public class SoundPlaybackService
{
    private bool _isPlaying;
    private readonly object _lock = new();
    
    public bool AllowInterruption { get; set; } = true;
    public bool ShowInterruptionMessage { get; set; } = true;

    public event EventHandler<SoundPlaybackRequestEventArgs>? PlaybackRequested;
    
    public bool IsPlaying
    {
        get { lock (_lock) { return _isPlaying; } }
        private set { lock (_lock) { _isPlaying = value; } }
    }

    public SoundPlaybackResult RequestPlayback(string soundFile, int volume)
    {
        if (string.IsNullOrWhiteSpace(soundFile))
        {
            return new SoundPlaybackResult 
            { 
                Success = false, 
                Reason = "Sound file path is empty",
                ShouldRefund = false
            };
        }

        if (!File.Exists(soundFile))
        {
            return new SoundPlaybackResult 
            { 
                Success = false, 
                Reason = $"Sound file does not exist: {soundFile}",
                ShouldRefund = false
            };
        }

        lock (_lock)
        {
            if (_isPlaying)
            {
                if (!AllowInterruption)
                {
                    // Sound is blocked because another sound is playing
                    Console.WriteLine($"[SoundPlaybackService] Sound blocked - AllowInterruption: {AllowInterruption}, ShowMessage: {ShowInterruptionMessage}");
                    return new SoundPlaybackResult
                    {
                        Success = false,
                        Reason = "Another sound is currently playing",
                        ShowMessage = ShowInterruptionMessage,
                        ShouldRefund = true // Refund points if sound was blocked
                    };
                }
                
                // Allow interruption - stop current sound
                Console.WriteLine($"[SoundPlaybackService] Interrupting current sound - AllowInterruption: {AllowInterruption}");
                _isPlaying = false;
            }

            _isPlaying = true;
        }

        // Trigger playback on UI thread
        PlaybackRequested?.Invoke(this, new SoundPlaybackRequestEventArgs
        {
            SoundFile = soundFile,
            Volume = volume
        });

        return new SoundPlaybackResult 
        { 
            Success = true, 
            Reason = "Sound playback started",
            ShouldRefund = false
        };
    }

    public void NotifyPlaybackEnded()
    {
        IsPlaying = false;
    }

    public void StopPlayback()
    {
        IsPlaying = false;
    }
}

public class SoundPlaybackRequestEventArgs : EventArgs
{
    public string SoundFile { get; init; } = string.Empty;
    public int Volume { get; init; }
}

public class SoundPlaybackResult
{
    public bool Success { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool ShowMessage { get; init; }
    public bool ShouldRefund { get; init; }
}
