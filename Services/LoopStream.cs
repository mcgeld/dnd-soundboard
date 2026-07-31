using NAudio.Wave;

namespace SoundBoard.Services;

/// <summary>
/// A WaveStream wrapper that infinitely loops its source stream.
/// </summary>
public class LoopStream : WaveStream
{
    private readonly WaveStream _sourceStream;

    public LoopStream(WaveStream sourceStream)
    {
        _sourceStream = sourceStream;
        EnableLooping = true;
    }

    public bool EnableLooping { get; set; }

    public override WaveFormat WaveFormat => _sourceStream.WaveFormat;
    public override long Length => _sourceStream.Length;

    public override long Position
    {
        get => _sourceStream.Position;
        set => _sourceStream.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int totalBytesRead = 0;

        while (totalBytesRead < count)
        {
            int bytesRead = _sourceStream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
            if (bytesRead == 0)
            {
                if (_sourceStream.Position == 0 || !EnableLooping)
                {
                    break;
                }
                _sourceStream.Position = 0;
            }
            else
            {
                totalBytesRead += bytesRead;
            }
        }
        return totalBytesRead;
    }
}
