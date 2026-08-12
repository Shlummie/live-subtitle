using System.Diagnostics;
using LiveSubtitle.Core;
using NAudio.CoreAudioApi;

namespace LiveSubtitle.App.Audio;

public sealed class AudioDeviceService
{
    public IReadOnlyList<AudioEndpointInfo> EnumerateEndpoints()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultRender = TryGetDefaultId(enumerator, DataFlow.Render);
        var defaultCapture = TryGetDefaultId(enumerator, DataFlow.Capture);
        var endpoints = new List<AudioEndpointInfo>();

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            endpoints.Add(new AudioEndpointInfo(
                device.ID,
                device.FriendlyName,
                AudioSourceKind.OutputDevice,
                device.ID == defaultRender,
                device.State.ToString()));
            device.Dispose();
        }

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            endpoints.Add(new AudioEndpointInfo(
                device.ID,
                device.FriendlyName,
                AudioSourceKind.InputDevice,
                device.ID == defaultCapture,
                device.State.ToString()));
            device.Dispose();
        }

        return endpoints.OrderByDescending(endpoint => endpoint.IsDefault).ThenBy(endpoint => endpoint.Name).ToList();
    }

    public IReadOnlyList<ProcessAudioInfo> EnumerateProcesses()
    {
        var ownId = Environment.ProcessId;
        var processes = new List<ProcessAudioInfo>();
        foreach (var process in Process.GetProcesses().Where(process => process.Id != ownId))
        {
            try
            {
                var path = process.MainModule?.FileName;
                var title = string.IsNullOrWhiteSpace(process.MainWindowTitle)
                    ? process.ProcessName
                    : process.MainWindowTitle;
                processes.Add(new ProcessAudioInfo(process.Id, process.ProcessName, path, title));
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                processes.Add(new ProcessAudioInfo(process.Id, process.ProcessName, null, process.ProcessName));
            }
            finally
            {
                process.Dispose();
            }
        }

        return processes
            .GroupBy(item => item.ProcessId)
            .Select(group => group.First())
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static string? FindDuplicateRouteWarning(IEnumerable<AudioSourceProfile> sources)
    {
        var enabled = sources.Where(source => source.Enabled).ToList();
        if (enabled.Any(source => source.Kind == AudioSourceKind.OutputDevice)
            && enabled.Any(source => source.Kind == AudioSourceKind.Process))
        {
            return "A selected app may also be present in whole-computer audio, which can create duplicate captions.";
        }

        var duplicates = enabled
            .GroupBy(source => (source.Kind, source.DeviceId, source.ProcessExecutablePath), StringTupleComparer.Instance)
            .FirstOrDefault(group => group.Count() > 1);
        return duplicates is null ? null : "Two enabled sources point to the same audio route.";
    }

    internal static MMDevice ResolveDevice(AudioSourceProfile profile)
    {
        var enumerator = new MMDeviceEnumerator();
        try
        {
            if (profile.FollowDefaultDevice || string.IsNullOrWhiteSpace(profile.DeviceId))
            {
                return enumerator.GetDefaultAudioEndpoint(
                    profile.Kind == AudioSourceKind.InputDevice ? DataFlow.Capture : DataFlow.Render,
                    Role.Multimedia);
            }
            return enumerator.GetDevice(profile.DeviceId);
        }
        finally
        {
            enumerator.Dispose();
        }
    }

    internal static string? ResolveDefaultDeviceId(AudioSourceKind kind)
    {
        using var enumerator = new MMDeviceEnumerator();
        return TryGetDefaultId(
            enumerator,
            kind == AudioSourceKind.InputDevice ? DataFlow.Capture : DataFlow.Render);
    }

    internal static Process? ResolveProcess(AudioSourceProfile profile)
    {
        var requireExactPath = !string.IsNullOrWhiteSpace(profile.ProcessExecutablePath);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (requireExactPath
                    && string.Equals(process.MainModule?.FileName, profile.ProcessExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    return process;
                }
                if (!requireExactPath
                    && !string.IsNullOrWhiteSpace(profile.ProcessName)
                    && string.Equals(process.ProcessName, profile.ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    return process;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            process.Dispose();
        }
        return null;
    }

    private static string? TryGetDefaultId(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            return device.ID;
        }
        catch
        {
            return null;
        }
    }

    private sealed class StringTupleComparer : IEqualityComparer<(AudioSourceKind Kind, string? DeviceId, string? Path)>
    {
        public static StringTupleComparer Instance { get; } = new();
        public bool Equals((AudioSourceKind Kind, string? DeviceId, string? Path) x, (AudioSourceKind Kind, string? DeviceId, string? Path) y) =>
            x.Kind == y.Kind
            && string.Equals(x.DeviceId, y.DeviceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((AudioSourceKind Kind, string? DeviceId, string? Path) obj) =>
            HashCode.Combine(obj.Kind, obj.DeviceId?.ToUpperInvariant(), obj.Path?.ToUpperInvariant());
    }
}
