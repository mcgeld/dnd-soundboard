using System;
using System.IO;
using System.Threading;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SoundBoard.Models;

namespace SoundBoard.Services;

/// <summary>
/// Manages playback, looping, and volume of an individual Track (.mp3, .ogg, .wav, etc.) with smooth 1.0s fading.
/// </summary>
public class TrackPlayer : IDisposable
{
    private WaveStream? _waveStream;
    private LoopStream? _loopStream;
    private VolumeSampleProvider? _volumeProvider;
    private ISampleProvider? _outputSampleProvider;

    private float _targetVolume = 1.0f;
    private float _currentVolume = 1.0f;
    private Timer? _fadeTimer;

    public Track Track { get; }
    public float Volume => _targetVolume;
    public bool IsLoaded => _outputSampleProvider != null;
    public ISampleProvider? SampleProvider => _outputSampleProvider;

    public TrackPlayer(Track track, WaveFormat masterWaveFormat)
    {
        Track = track;
        Initialize(masterWaveFormat);
    }

    private void Initialize(WaveFormat masterWaveFormat)
    {
        if (!File.Exists(Track.FilePath))
        {
            Console.WriteLine($"[TrackPlayer Warning] Track file not found: '{Track.FilePath}'");
            return;
        }

        try
        {
            string ext = Path.GetExtension(Track.FilePath).ToLowerInvariant();
            if (ext == ".ogg")
            {
                _waveStream = new VorbisWaveReader(Track.FilePath);
            }
            else
            {
                _waveStream = new AudioFileReader(Track.FilePath);
            }

            _loopStream = new LoopStream(_waveStream);

            ISampleProvider sampleProvider = _loopStream.ToSampleProvider();

            if (sampleProvider.WaveFormat.Channels == 1 && masterWaveFormat.Channels == 2)
            {
                sampleProvider = new MonoToStereoSampleProvider(sampleProvider);
            }

            if (sampleProvider.WaveFormat.SampleRate != masterWaveFormat.SampleRate)
            {
                sampleProvider = new WdlResamplingSampleProvider(sampleProvider, masterWaveFormat.SampleRate);
            }

            _volumeProvider = new VolumeSampleProvider(sampleProvider)
            {
                Volume = _currentVolume
            };

            _outputSampleProvider = _volumeProvider;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TrackPlayer Error] Could not initialize track '{Track.FileName}': {ex.Message}");
        }
    }

    public void SetVolume(float targetVolume, bool immediate = false)
    {
        _targetVolume = Math.Clamp(targetVolume, 0.0f, 1.0f);

        if (immediate || _volumeProvider == null)
        {
            _fadeTimer?.Dispose();
            _fadeTimer = null;
            _currentVolume = _targetVolume;
            if (_volumeProvider != null) _volumeProvider.Volume = _currentVolume;
            return;
        }

        // Smooth 1.0 second (1000ms) fade transition (50 steps @ 20ms interval)
        _fadeTimer?.Dispose();
        int steps = 50;
        int intervalMs = 20;
        int currentStep = 0;
        float startVol = _currentVolume;
        float delta = (_targetVolume - startVol) / steps;

        _fadeTimer = new Timer(_ =>
        {
            currentStep++;
            _currentVolume = Math.Clamp(startVol + (delta * currentStep), 0.0f, 1.0f);

            if (_volumeProvider != null)
            {
                _volumeProvider.Volume = _currentVolume;
            }

            if (currentStep >= steps)
            {
                _currentVolume = _targetVolume;
                if (_volumeProvider != null) _volumeProvider.Volume = _currentVolume;
                _fadeTimer?.Dispose();
                _fadeTimer = null;
            }
        }, null, 0, intervalMs);
    }

    public void Dispose()
    {
        _fadeTimer?.Dispose();
        _fadeTimer = null;

        _outputSampleProvider = null;
        _volumeProvider = null;

        if (_loopStream != null)
        {
            _loopStream.Dispose();
            _loopStream = null;
        }

        if (_waveStream != null)
        {
            _waveStream.Dispose();
            _waveStream = null;
        }
    }
}
