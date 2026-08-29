using System.Collections.Generic;

namespace GuideAssistant.Services;

internal sealed class BlockingPcmStream : Stream
{
    private const int MaxBufferedBytes = 4 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly Queue<Chunk> _chunks = new();
    private Chunk? _current;
    private bool _completed;
    private bool _disposed;
    private int _bufferedBytes;

    private sealed record Chunk(byte[] Data, int Offset, int Count);

    public void Append(byte[] pcmData)
    {
        if (pcmData.Length == 0) return;

        var copy = new byte[pcmData.Length];
        Buffer.BlockCopy(pcmData, 0, copy, 0, pcmData.Length);

        lock (_gate)
        {
            while (!_disposed && !_completed && _bufferedBytes + copy.Length > MaxBufferedBytes)
            {
                Monitor.Wait(_gate);
            }

            if (_disposed || _completed) return;

            _chunks.Enqueue(new(copy, 0, copy.Length));
            _bufferedBytes += copy.Length;
            Monitor.PulseAll(_gate);
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            if (_completed) return;
            _completed = true;
            Monitor.PulseAll(_gate);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || buffer.Length - offset < count) throw new ArgumentOutOfRangeException();
        if (count == 0) return 0;

        lock (_gate)
        {
            while (true)
            {
                var chunk = _current;
                if (chunk == null && _chunks.Count > 0)
                {
                    chunk = _current = _chunks.Dequeue();
                }

                if (chunk != null)
                {
                    var bytesToCopy = Math.Min(count, chunk.Count);
                    Buffer.BlockCopy(chunk.Data, chunk.Offset, buffer, offset, bytesToCopy);
                    _bufferedBytes -= bytesToCopy;

                    if (bytesToCopy < chunk.Count)
                    {
                        _current = chunk with { Offset = chunk.Offset + bytesToCopy, Count = chunk.Count - bytesToCopy };
                    }
                    else
                    {
                        _current = null;
                    }

                    Monitor.PulseAll(_gate);
                    return bytesToCopy;
                }

                if (_completed || _disposed) return 0;
                Monitor.Wait(_gate);
            }
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;

        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _completed = true;
            Monitor.PulseAll(_gate);
        }

        base.Dispose(disposing);
    }
}
