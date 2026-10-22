using System.Text;

namespace PracticeTimer.Core.Tests.Support;

/// <summary>Reads a PCM or float WAV file as mono float samples (channels averaged).</summary>
public static class WavReader
{
    public static (float[] Samples, int Rate) Read(string path)
    {
        var b = File.ReadAllBytes(path);
        if (b.Length < 12 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE")
            throw new InvalidDataException($"{path} is not a WAV file");
        int format = 0, channels = 0, rate = 0, bits = 0, dataAt = -1, dataLen = 0;
        for (var p = 12; p + 8 <= b.Length;)
        {
            var id = Encoding.ASCII.GetString(b, p, 4);
            var len = BitConverter.ToInt32(b, p + 4);
            if (id == "fmt ")
            {
                format = BitConverter.ToUInt16(b, p + 8);
                channels = BitConverter.ToUInt16(b, p + 10);
                rate = BitConverter.ToInt32(b, p + 12);
                bits = BitConverter.ToUInt16(b, p + 22);
                if (format == 0xFFFE) format = BitConverter.ToUInt16(b, p + 32); // extensible: sub-format code
            }
            else if (id == "data") { dataAt = p + 8; dataLen = Math.Min(len < 0 ? int.MaxValue : len, b.Length - dataAt); break; }
            p += 8 + len + (len & 1);
        }
        if (dataAt < 0 || channels < 1) throw new InvalidDataException($"{path} has no audio data");
        var bytes = bits / 8;
        var frames = dataLen / (bytes * channels);
        var mono = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            double sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var o = dataAt + (i * channels + c) * bytes;
                sum += (format, bits) switch
                {
                    (1, 16) => BitConverter.ToInt16(b, o) / 32768.0,
                    (1, 24) => ((b[o] | b[o + 1] << 8 | (sbyte)b[o + 2] << 16)) / 8388608.0,
                    (1, 32) => BitConverter.ToInt32(b, o) / 2147483648.0,
                    (3, 32) => BitConverter.ToSingle(b, o),
                    _ => throw new InvalidDataException($"Unsupported WAV format {format}/{bits}-bit"),
                };
            }
            mono[i] = (float)(sum / channels);
        }
        return (mono, rate);
    }
}
