using System.IO;
using NAudio.Wave;

namespace PomogatorLauncher;

/// <summary>
/// Непрерывный захват звука с ПК (WASAPI loopback) в кольцевой буфер.
/// </summary>
internal sealed class LoopbackRingBuffer : IDisposable
{
    private readonly object _lock = new();
    private readonly byte[] _buffer;
    private readonly WaveFormat _format;
    private WasapiLoopbackCapture? _capture;
    private int _writePos;
    private bool _full;
    private long _totalBytesWritten;
    private bool _disposed;

    public LoopbackRingBuffer(int bufferSeconds = 14)
    {
        _capture = new WasapiLoopbackCapture();
        _format = _capture.WaveFormat;
        var cap = _format.AverageBytesPerSecond * bufferSeconds;
        _buffer = new byte[Math.Max(cap, _format.BlockAlign * 1024)];
    }

    public WaveFormat WaveFormat => _format;

    public void Start()
    {
        ThrowIfDisposed();
        var capture = _capture ?? throw new InvalidOperationException("Capture disposed.");
        capture.DataAvailable += OnDataAvailable;
        capture.StartRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        lock (_lock)
        {
            for (var i = 0; i < e.BytesRecorded; i++)
            {
                _buffer[_writePos] = e.Buffer[i];
                _writePos++;
                if (_writePos >= _buffer.Length)
                {
                    _writePos = 0;
                    _full = true;
                }

                _totalBytesWritten++;
            }
        }
    }

    /// <summary>
    /// Последние <paramref name="seconds"/> секунд в виде WAV для ShazamIO.
    /// </summary>
    public byte[] GetLastSecondsAsWav(int seconds)
    {
        ThrowIfDisposed();
        var needRaw = _format.AverageBytesPerSecond * seconds;
        needRaw = (needRaw / _format.BlockAlign) * _format.BlockAlign;
        if (needRaw <= 0)
            return Array.Empty<byte>();

        byte[] pcm;
        lock (_lock)
        {
            var available = _full ? _buffer.Length : _writePos;
            var n = Math.Min(needRaw, available);
            n = (n / _format.BlockAlign) * _format.BlockAlign;
            if (n <= 0)
                return Array.Empty<byte>();

            pcm = new byte[n];
            if (!_full)
            {
                Buffer.BlockCopy(_buffer, _writePos - n, pcm, 0, n);
            }
            else
            {
                var start = (_writePos - n + _buffer.Length) % _buffer.Length;
                if (start + n <= _buffer.Length)
                    Buffer.BlockCopy(_buffer, start, pcm, 0, n);
                else
                {
                    var first = _buffer.Length - start;
                    Buffer.BlockCopy(_buffer, start, pcm, 0, first);
                    Buffer.BlockCopy(_buffer, 0, pcm, first, n - first);
                }
            }
        }

        var tmp = Path.Combine(Path.GetTempPath(), "pom_snap_" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using (var w = new WaveFileWriter(tmp, _format))
                w.Write(pcm, 0, pcm.Length);
            return File.ReadAllBytes(tmp);
        }
        finally
        {
            try
            {
                File.Delete(tmp);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    public void Stop()
    {
        var capture = _capture;
        if (capture == null) return;
        try
        {
            capture.StopRecording();
        }
        catch
        {
            /* ignore */
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var capture = _capture;
        _capture = null;
        if (capture != null)
        {
            try
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.StopRecording();
                capture.Dispose();
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LoopbackRingBuffer));
    }
}
