using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace PracticeTimer.Platform;

public sealed record MicDevice(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Receives one block of mono float samples. The span is only valid during the call.</summary>
public delegate void SamplesHandler(ReadOnlySpan<float> mono);

/// <summary>WASAPI capture that hands mono float samples at the device's own rate to a callback.</summary>
public sealed class MicCapture : IDisposable
{
    private enum Sample { Float32, Float64, Pcm8, Pcm16, Pcm24, Pcm32 }

    private WasapiCapture? _capture;
    private MMDevice? _device;
    private SamplesHandler? _onSamples;
    private float[] _mono = new float[4096];
    private long _lastDataTimestamp;
    private bool _hasData;

    /// <summary>Raised only when a capture ends on its own (device lost, driver error). Never for a deliberate Stop or restart.</summary>
    public event Action<Exception?>? Stopped;

    public bool IsRunning => _capture != null;

    /// <summary>True once the current capture has delivered at least one block.</summary>
    public bool HasData => Volatile.Read(ref _hasData);

    /// <summary>How long since audio last arrived (or since capture started, before the first block). Zero when not capturing.</summary>
    public TimeSpan SilenceAge => _capture == null ? TimeSpan.Zero
        : TimeSpan.FromSeconds((double)(System.Diagnostics.Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastDataTimestamp)) / System.Diagnostics.Stopwatch.Frequency);

    public static List<MicDevice> List()
    {
        using var e = new MMDeviceEnumerator();
        return e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => new MicDevice(d.ID, d.FriendlyName)).ToList();
    }

    public static string? DefaultId()
    {
        try
        {
            using var e = new MMDeviceEnumerator();
            return e.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console).ID;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Starts capturing; returns the device's sample rate.</summary>
    public int Start(string deviceId, SamplesHandler onSamples)
    {
        Stop();
        using var e = new MMDeviceEnumerator();
        MMDevice? device = null;
        WasapiCapture? capture = null;
        try
        {
            device = e.GetDevice(deviceId);
            capture = new WasapiCapture(device) { ShareMode = AudioClientShareMode.Shared };
            var wf = capture.WaveFormat is WaveFormatExtensible ext ? ext.ToStandardWaveFormat() : capture.WaveFormat;
            var kind = KindOf(wf);
            var channels = Math.Max(1, wf.Channels);
            _onSamples = onSamples;
            Interlocked.Exchange(ref _lastDataTimestamp, System.Diagnostics.Stopwatch.GetTimestamp());
            Volatile.Write(ref _hasData, false);
            var mine = capture;
            capture.DataAvailable += (_, a) => Convert(a.Buffer, a.BytesRecorded, kind, channels);
            capture.RecordingStopped += (_, a) =>
            {
                // Stop() clears _capture first, so a capture we ended ourselves never reports.
                if (ReferenceEquals(mine, _capture)) Stopped?.Invoke(a.Exception);
            };
            _device = device;
            _capture = capture;
            capture.StartRecording();
            return wf.SampleRate;
        }
        catch
        {
            _capture = null;
            capture?.Dispose();
            device?.Dispose();
            _device = null;
            throw;
        }
    }

    private static Sample KindOf(WaveFormat wf) => (wf.Encoding, wf.BitsPerSample) switch
    {
        (WaveFormatEncoding.IeeeFloat, 32) => Sample.Float32,
        (WaveFormatEncoding.IeeeFloat, 64) => Sample.Float64,
        (WaveFormatEncoding.Pcm, 8) => Sample.Pcm8,
        (WaveFormatEncoding.Pcm, 16) => Sample.Pcm16,
        (WaveFormatEncoding.Pcm, 24) => Sample.Pcm24,
        (WaveFormatEncoding.Pcm, 32) => Sample.Pcm32,
        _ => throw new NotSupportedException($"Unsupported microphone format: {wf.Encoding} {wf.BitsPerSample}-bit"),
    };

    private void Convert(byte[] buf, int bytes, Sample kind, int channels)
    {
        Interlocked.Exchange(ref _lastDataTimestamp, System.Diagnostics.Stopwatch.GetTimestamp());
        Volatile.Write(ref _hasData, true);
        var bytesPer = kind switch { Sample.Pcm8 => 1, Sample.Pcm16 => 2, Sample.Pcm24 => 3, Sample.Float64 => 8, _ => 4 };
        var frames = Math.Min(bytes, buf.Length) / (bytesPer * channels);
        if (frames == 0) return;
        if (_mono.Length < frames) _mono = new float[frames];
        var src = buf.AsSpan(0, frames * bytesPer * channels);
        var mono = _mono.AsSpan(0, frames);

        if (kind == Sample.Float32)
        {
            var f = MemoryMarshal.Cast<byte, float>(src);
            if (channels == 1) f.CopyTo(mono);
            else
                for (var i = 0; i < frames; i++)
                {
                    float sum = 0;
                    for (var c = 0; c < channels; c++) sum += f[i * channels + c];
                    mono[i] = sum / channels;
                }
        }
        else
        {
            for (var i = 0; i < frames; i++)
            {
                float sum = 0;
                for (var c = 0; c < channels; c++)
                {
                    var o = (i * channels + c) * bytesPer;
                    sum += kind switch
                    {
                        Sample.Float64 => (float)BitConverter.ToDouble(src[o..]),
                        Sample.Pcm8 => (src[o] - 128) / 128f,
                        Sample.Pcm16 => BitConverter.ToInt16(src[o..]) / 32768f,
                        Sample.Pcm24 => ((src[o] << 8 | src[o + 1] << 16 | src[o + 2] << 24) >> 8) / 8388608f,
                        _ => BitConverter.ToInt32(src[o..]) / 2147483648f,
                    };
                }
                mono[i] = sum / channels;
            }
        }
        _onSamples?.Invoke(mono);
    }

    public void Stop()
    {
        var c = _capture;
        _capture = null;
        if (c != null)
        {
            try { c.StopRecording(); } catch (Exception) { }
            c.Dispose();
        }
        _device?.Dispose();
        _device = null;
    }

    public void Dispose() => Stop();
}

/// <summary>Raises <see cref="Changed"/> when capture devices are added, removed or change state.</summary>
public sealed class MicWatcher : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    public event Action? Changed;

    public MicWatcher() => _enumerator.RegisterEndpointNotificationCallback(this);

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Changed?.Invoke();
    public void OnDeviceAdded(string pwstrDeviceId) => Changed?.Invoke();
    public void OnDeviceRemoved(string deviceId) => Changed?.Invoke();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(this);
        _enumerator.Dispose();
    }
}
