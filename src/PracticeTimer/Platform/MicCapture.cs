using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace PracticeTimer.Platform;

public sealed record MicDevice(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>WASAPI capture that hands mono float samples at the device's own rate to a callback.</summary>
public sealed class MicCapture : IDisposable
{
    private WasapiCapture? _capture;
    private MMDevice? _device;
    private Action<float[], int>? _onSamples;

    public int SampleRate { get; private set; }
    public event Action<Exception?>? Stopped;

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
    public int Start(string deviceId, Action<float[], int> onSamples)
    {
        Stop();
        using var e = new MMDeviceEnumerator();
        _device = e.GetDevice(deviceId);
        _capture = new WasapiCapture(_device) { ShareMode = AudioClientShareMode.Shared };
        var wf = _capture.WaveFormat is WaveFormatExtensible ext ? ext.ToStandardWaveFormat() : _capture.WaveFormat;
        SampleRate = wf.SampleRate;
        _onSamples = onSamples;
        _capture.DataAvailable += (_, a) => Convert(a.Buffer, a.BytesRecorded, wf);
        _capture.RecordingStopped += (_, a) => Stopped?.Invoke(a.Exception);
        _capture.StartRecording();
        return SampleRate;
    }

    private void Convert(byte[] buf, int bytes, WaveFormat wf)
    {
        var ch = Math.Max(1, wf.Channels);
        var bytesPer = wf.BitsPerSample / 8;
        var frames = bytes / (bytesPer * ch);
        var mono = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            float sum = 0;
            for (var c = 0; c < ch; c++)
            {
                var o = (i * ch + c) * bytesPer;
                sum += wf.Encoding == WaveFormatEncoding.IeeeFloat ? BitConverter.ToSingle(buf, o)
                    : bytesPer == 2 ? BitConverter.ToInt16(buf, o) / 32768f
                    : bytesPer == 3 ? ((buf[o] << 8 | buf[o + 1] << 16 | buf[o + 2] << 24) >> 8) / 8388608f
                    : BitConverter.ToInt32(buf, o) / 2147483648f;
            }
            mono[i] = sum / ch;
        }
        _onSamples?.Invoke(mono, SampleRate);
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
