using NAudio.Wave;

namespace GuideAssistant.Services;

/// <summary>
/// Converts native WASAPI mix-format samples into the 16 kHz, 16-bit mono PCM
/// required by System.Speech. Maintains the resampling phase across callbacks.
/// </summary>
internal sealed class LoopbackPcmConverter
{
    private readonly WaveFormat _format;
    private readonly bool _floatingPoint;
    private readonly int _sampleBytes;
    private readonly double _step;
    private double _nextPosition;

    internal LoopbackPcmConverter(WaveFormat format)
    {
        _format = format;
        var encoding = format.Encoding;
        if (format is WaveFormatExtensible extensible)
        {
            var floatGuid = new Guid("00000003-0000-0010-8000-00aa00389b71");
            var pcmGuid = new Guid("00000001-0000-0010-8000-00aa00389b71");
            if (extensible.SubFormat == floatGuid) encoding = WaveFormatEncoding.IeeeFloat;
            else if (extensible.SubFormat == pcmGuid) encoding = WaveFormatEncoding.Pcm;
        }
        _floatingPoint = encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32;
        if (!_floatingPoint && !(encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16))
            throw new NotSupportedException($"Unsupported loopback format: {format}");
        if (format.SampleRate <= 0 || format.Channels <= 0)
            throw new NotSupportedException($"Invalid loopback format: {format}");
        _sampleBytes = format.BitsPerSample / 8;
        _step = (double)format.SampleRate / 16000.0;
    }

    internal byte[] Convert(byte[] input, int length)
    {
        var frames = length / _format.BlockAlign;
        if (frames == 0) return Array.Empty<byte>();
        var mono = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int ch = 0; ch < _format.Channels; ch++)
            {
                int offset = f * _format.BlockAlign + ch * _sampleBytes;
                sum += _floatingPoint
                    ? BitConverter.ToSingle(input, offset)
                    : BitConverter.ToInt16(input, offset) / 32768f;
            }
            mono[f] = sum / _format.Channels;
        }

        var output = new List<byte>(frames * 2);
        while (_nextPosition < frames)
        {
            int index = (int)_nextPosition;
            double fraction = _nextPosition - index;
            float current = mono[index];
            float following = mono[Math.Min(index + 1, frames - 1)];
            float sample = Math.Clamp((float)(current + (following - current) * fraction), -1f, 1f);
            short pcm = (short)Math.Round(sample * (sample < 0 ? 32768f : 32767f));
            output.Add(unchecked((byte)pcm));
            output.Add(unchecked((byte)(pcm >> 8)));
            _nextPosition += _step;
        }
        _nextPosition -= frames;
        return output.ToArray();
    }
}
