namespace PracticeTimer.Core.Analysis;

/// <summary>One analysis frame. <see cref="Start"/> derives from the sample count, never the wall clock.</summary>
public sealed record AudioFrame(TimeSpan Start, float[] Samples, int SampleRate)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);
    public TimeSpan End => Start + Duration;
}

public sealed class FrameSlicer
{
    private readonly int _rate;
    private readonly int _frameSamples;
    private readonly float[] _buf;
    private int _fill;
    private long _emitted;

    public FrameSlicer(int sampleRate, TimeSpan frameLength)
    {
        _rate = sampleRate;
        _frameSamples = Math.Max(1, (int)Math.Round(sampleRate * frameLength.TotalSeconds));
        _buf = new float[_frameSamples];
    }

    public void Push(ReadOnlySpan<float> samples, Action<AudioFrame> onFrame)
    {
        while (!samples.IsEmpty)
        {
            var take = Math.Min(_frameSamples - _fill, samples.Length);
            samples[..take].CopyTo(_buf.AsSpan(_fill));
            _fill += take;
            samples = samples[take..];
            if (_fill < _frameSamples) continue;
            var start = TimeSpan.FromSeconds((double)(_emitted * _frameSamples) / _rate);
            onFrame(new AudioFrame(start, (float[])_buf.Clone(), _rate));
            _emitted++;
            _fill = 0;
        }
    }
}
