using System;
using System.Collections.Generic;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SoundBoard.Models;

namespace SoundBoard.Services;

/// <summary>
/// Audio engine managing output streams and runtime Channel playback states with smooth volume fading.
/// </summary>
public class AudioEngine : IDisposable
{
    private WaveOutEvent? _waveOut;
    private readonly MixingSampleProvider _masterMixer;
    private readonly WaveFormat _masterWaveFormat;

    private readonly Channel[] _channels = new Channel[8];
    private readonly List<TrackPlayer>[] _activePlayers = new List<TrackPlayer>[8];
    private readonly Timer?[] _faderFadeTimers = new Timer?[8];
    private readonly Timer?[][] _knobFadeTimers = new Timer?[8][];

    public IReadOnlyList<Channel> Channels => _channels;

    public AudioEngine()
    {
        _masterWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        _masterMixer = new MixingSampleProvider(_masterWaveFormat)
        {
            ReadFully = true
        };

        for (int i = 0; i < 8; i++)
        {
            _channels[i] = new Channel(i);
            _activePlayers[i] = new List<TrackPlayer>();
            _knobFadeTimers[i] = new Timer?[3];
        }

        InitializePlayback();
    }

    private void InitializePlayback()
    {
        try
        {
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_masterMixer);
            _waveOut.Play();
            Console.WriteLine("[AudioEngine] NAudio WaveOut pipeline active (44100Hz Stereo IEEE Float).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioEngine Error] Could not initialize audio output device: {ex.Message}");
        }
    }

    public void LoadStemToChannel(int channelIndex, Stem? stem, bool immediate = true)
    {
        if (channelIndex < 0 || channelIndex >= 8) return;

        var channel = _channels[channelIndex];
        ClearChannelAudio(channelIndex);

        channel.LoadedStem = stem;

        if (stem != null && stem.Tracks.Count > 0)
        {
            // Freshly assigned channels default to MUTED for safe level adjustment
            channel.IsMuted = true;
            Console.WriteLine($"[AudioEngine] Channel {channelIndex} loaded stem '[{stem.Name}]' ({stem.Tracks.Count} tracks) -> Default MUTED.");

            for (int i = 0; i < stem.Tracks.Count && i < 3; i++)
            {
                var player = new TrackPlayer(stem.Tracks[i], _masterWaveFormat);
                _activePlayers[channelIndex].Add(player);

                if (player.SampleProvider != null)
                {
                    _masterMixer.AddMixerInput(player.SampleProvider);
                }
            }
        }
        else
        {
            channel.IsMuted = false;
            Console.WriteLine($"[AudioEngine] Channel {channelIndex} cleared (unassigned).");
        }

        UpdateChannelEffectiveVolumes(channelIndex, immediate);
    }

    public void SetMasterVolume(int channelIndex, float volume, bool immediate = true)
    {
        if (channelIndex < 0 || channelIndex >= 8) return;

        _faderFadeTimers[channelIndex]?.Dispose();
        _faderFadeTimers[channelIndex] = null;

        var channel = _channels[channelIndex];
        channel.MasterVolume = Math.Clamp(volume, 0.0f, 1.0f);
        UpdateChannelEffectiveVolumes(channelIndex, immediate);
    }

    public void FadeMasterVolume(int channelIndex, float startVolume, float targetVolume, int durationMs, Action? onComplete = null)
    {
        if (channelIndex < 0 || channelIndex >= 8) return;

        _faderFadeTimers[channelIndex]?.Dispose();

        var channel = _channels[channelIndex];
        int steps = 50;
        int intervalMs = Math.Max(10, durationMs / steps);
        int currentStep = 0;

        _faderFadeTimers[channelIndex] = new Timer(_ =>
        {
            currentStep++;
            float progress = (float)currentStep / steps;
            float currentVal = startVolume + (targetVolume - startVolume) * progress;

            channel.MasterVolume = Math.Clamp(currentVal, 0.0f, 1.0f);
            UpdateChannelEffectiveVolumes(channelIndex, immediate: true);

            if (currentStep >= steps)
            {
                _faderFadeTimers[channelIndex]?.Dispose();
                _faderFadeTimers[channelIndex] = null;
                channel.MasterVolume = targetVolume;
                UpdateChannelEffectiveVolumes(channelIndex, immediate: true);
                onComplete?.Invoke();
            }
        }, null, intervalMs, intervalMs);
    }

    public void SetTrackVolume(int channelIndex, int trackIndex, float volume, bool immediate = true)
    {
        if (channelIndex < 0 || channelIndex >= 8) return;
        if (trackIndex < 0 || trackIndex >= 3) return;

        _knobFadeTimers[channelIndex][trackIndex]?.Dispose();
        _knobFadeTimers[channelIndex][trackIndex] = null;

        var channel = _channels[channelIndex];
        channel.TrackVolumes[trackIndex] = Math.Clamp(volume, 0.0f, 1.0f);
        UpdateChannelEffectiveVolumes(channelIndex, immediate);
    }

    public void FadeTrackVolume(int channelIndex, int trackIndex, float startVolume, float targetVolume, int durationMs, Action? onComplete = null)
    {
        if (channelIndex < 0 || channelIndex >= 8) return;
        if (trackIndex < 0 || trackIndex >= 3) return;

        _knobFadeTimers[channelIndex][trackIndex]?.Dispose();

        var channel = _channels[channelIndex];
        int steps = 50;
        int intervalMs = Math.Max(10, durationMs / steps);
        int currentStep = 0;

        _knobFadeTimers[channelIndex][trackIndex] = new Timer(_ =>
        {
            currentStep++;
            float progress = (float)currentStep / steps;
            float currentVal = startVolume + (targetVolume - startVolume) * progress;

            channel.TrackVolumes[trackIndex] = Math.Clamp(currentVal, 0.0f, 1.0f);
            UpdateChannelEffectiveVolumes(channelIndex, immediate: true);

            if (currentStep >= steps)
            {
                _knobFadeTimers[channelIndex][trackIndex]?.Dispose();
                _knobFadeTimers[channelIndex][trackIndex] = null;
                channel.TrackVolumes[trackIndex] = targetVolume;
                UpdateChannelEffectiveVolumes(channelIndex, immediate: true);
                onComplete?.Invoke();
            }
        }, null, intervalMs, intervalMs);
    }

    public bool ToggleMute(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= 8) return false;

        var channel = _channels[channelIndex];
        channel.IsMuted = !channel.IsMuted;
        UpdateChannelEffectiveVolumes(channelIndex, immediate: false);
        return channel.IsMuted;
    }

    public void MuteAllChannels()
    {
        for (int i = 0; i < 8; i++)
        {
            if (_channels[i].LoadedStem != null && !_channels[i].IsMuted)
            {
                _channels[i].IsMuted = true;
                UpdateChannelEffectiveVolumes(i, immediate: false);
            }
        }
    }

    public void RestoreUnmutedSnapshot(bool[] unmutedChannels)
    {
        if (unmutedChannels == null || unmutedChannels.Length < 8) return;

        for (int i = 0; i < 8; i++)
        {
            if (_channels[i].LoadedStem != null && unmutedChannels[i])
            {
                _channels[i].IsMuted = false;
                UpdateChannelEffectiveVolumes(i, immediate: false);
            }
        }
    }

    public float GetEffectiveVolume(int channelIndex, int trackIndex)
    {
        if (channelIndex < 0 || channelIndex >= 8) return 0.0f;
        if (trackIndex < 0 || trackIndex >= 3) return 0.0f;

        var channel = _channels[channelIndex];
        if (channel.LoadedStem == null || channel.IsMuted) return 0.0f;

        return channel.MasterVolume * channel.TrackVolumes[trackIndex];
    }

    public void UpdateChannelEffectiveVolumes(int channelIndex, bool immediate = false)
    {
        var channel = _channels[channelIndex];
        var players = _activePlayers[channelIndex];

        for (int i = 0; i < players.Count && i < 3; i++)
        {
            float effectiveVol = GetEffectiveVolume(channelIndex, i);
            players[i].SetVolume(effectiveVol, immediate);
        }
    }

    private void ClearChannelAudio(int channelIndex)
    {
        _faderFadeTimers[channelIndex]?.Dispose();
        _faderFadeTimers[channelIndex] = null;

        for (int t = 0; t < 3; t++)
        {
            _knobFadeTimers[channelIndex][t]?.Dispose();
            _knobFadeTimers[channelIndex][t] = null;
        }

        var players = _activePlayers[channelIndex];
        foreach (var player in players)
        {
            if (player.SampleProvider != null)
            {
                _masterMixer.RemoveMixerInput(player.SampleProvider);
            }
            player.Dispose();
        }
        players.Clear();
    }

    public void Dispose()
    {
        for (int i = 0; i < 8; i++)
        {
            ClearChannelAudio(i);
        }

        if (_waveOut != null)
        {
            _waveOut.Stop();
            _waveOut.Dispose();
            _waveOut = null;
        }

        Console.WriteLine("[AudioEngine] Disposed cleanly.");
    }
}
