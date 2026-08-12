// Process-loopback activation follows the official Windows ApplicationLoopback sample and the
// equivalent NAudio 3 implementation. It is intentionally isolated here so the application can
// keep stable NAudio 2.3 for all endpoint capture. Requires Windows 10 2004 (build 19041) or later.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using LiveSubtitle.Core;

namespace LiveSubtitle.App.Audio;

public sealed class ProcessLoopbackCapture : IAudioSourceCapture
{
    private NativeProcessLoopback? native;
    private Process? process;

    public ProcessLoopbackCapture(AudioSourceProfile profile)
    {
        Profile = profile;
    }

    public AudioSourceProfile Profile { get; }
    public event EventHandler<AudioChunk>? AudioAvailable;
    public event EventHandler<string>? Faulted;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            throw new PlatformNotSupportedException("Application audio capture requires Windows 10 version 2004 or newer.");
        }

        process = AudioDeviceService.ResolveProcess(Profile)
                  ?? throw new InvalidOperationException(
                      $"{Profile.Label} is not running. LiveSubtitle will reattach when it starts again.");
        native = await NativeProcessLoopback.CreateAsync((uint)process.Id, cancellationToken).ConfigureAwait(false);
        native.DataAvailable += OnNativeData;
        native.Faulted += OnNativeFaulted;
        native.Start();
    }

    public Task StopAsync()
    {
        native?.Stop();
        return Task.CompletedTask;
    }

    private void OnNativeData(object? sender, float[] samples)
    {
        var normalized = AudioProcessing.ToMono16Khz(samples, 44_100, 2, Profile.Gain);
        AudioAvailable?.Invoke(this, new AudioChunk(
            Profile.Id,
            Profile.Label,
            Profile.ColorHex,
            normalized,
            DateTimeOffset.UtcNow,
            AudioProcessing.RootMeanSquare(normalized)));
    }

    private void OnNativeFaulted(object? sender, string detail) =>
        Faulted?.Invoke(this, $"{Profile.Label}: {detail}");

    public async ValueTask DisposeAsync()
    {
        if (native is not null)
        {
            native.DataAvailable -= OnNativeData;
            native.Faulted -= OnNativeFaulted;
            await native.DisposeAsync().ConfigureAwait(false);
            native = null;
        }
        process?.Dispose();
        process = null;
    }
}

internal sealed class NativeProcessLoopback : IAsyncDisposable
{
    private const string VirtualAudioDeviceProcessLoopback = "VAD\\Process_Loopback";
    private const long BufferDuration = 400_000; // 40 ms in 100 ns units.
    private static readonly Guid AudioClientId = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid CaptureClientId = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    private static readonly Guid CompletionHandlerId = new("41D949AB-9862-444A-80F6-C261334DA5EB");
    private static readonly StrategyBasedComWrappers ComWrappers = new();

    private readonly IAudioClientNative audioClient;
    private readonly EventWaitHandle frameEvent = new(false, EventResetMode.AutoReset);
    private CancellationTokenSource? cancellation;
    private Task? captureTask;
    private IAudioCaptureClientNative? captureClient;

    private NativeProcessLoopback(IAudioClientNative audioClient)
    {
        this.audioClient = audioClient;
    }

    public event EventHandler<float[]>? DataAvailable;
    public event EventHandler<string>? Faulted;

    public static async Task<NativeProcessLoopback> CreateAsync(uint processId, CancellationToken cancellationToken)
    {
        var activationParams = new AudioClientActivationParamsNative
        {
            ActivationType = AudioClientActivationTypeNative.ProcessLoopback,
            ProcessLoopbackParams = new AudioClientProcessLoopbackParamsNative
            {
                TargetProcessId = processId,
                ProcessLoopbackMode = ProcessLoopbackModeNative.IncludeTargetProcessTree
            }
        };

        var paramsSize = Marshal.SizeOf<AudioClientActivationParamsNative>();
        var paramsPointer = Marshal.AllocHGlobal(paramsSize);
        var variantPointer = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariantBlobHeader>());
        try
        {
            Marshal.StructureToPtr(activationParams, paramsPointer, false);
            Marshal.StructureToPtr(new PropVariantBlobHeader
            {
                Vt = 65, // VT_BLOB
                BlobSize = (uint)paramsSize,
                BlobData = paramsPointer
            }, variantPointer, false);

            var client = await ActivateAsync(variantPointer, cancellationToken).ConfigureAwait(false);
            return new NativeProcessLoopback(client);
        }
        finally
        {
            Marshal.FreeHGlobal(variantPointer);
            Marshal.FreeHGlobal(paramsPointer);
        }
    }

    public void Start()
    {
        if (captureTask is not null) throw new InvalidOperationException("Capture is already running.");
        var format = new WaveFormatExNative
        {
            FormatTag = 3, // IEEE float
            Channels = 2,
            SamplesPerSecond = 44_100,
            AverageBytesPerSecond = 44_100 * 2 * sizeof(float),
            BlockAlign = 2 * sizeof(float),
            BitsPerSample = 32,
            ExtraSize = 0
        };
        var formatPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatExNative>());
        try
        {
            Marshal.StructureToPtr(format, formatPointer, false);
            ThrowIfFailed(audioClient.Initialize(
                AudioClientShareModeNative.Shared,
                AudioClientStreamFlagsNative.Loopback | AudioClientStreamFlagsNative.EventCallback,
                BufferDuration,
                0,
                formatPointer,
                Guid.Empty));
        }
        finally
        {
            Marshal.FreeHGlobal(formatPointer);
        }

        ThrowIfFailed(audioClient.SetEventHandle(frameEvent.SafeWaitHandle.DangerousGetHandle()));
        ThrowIfFailed(audioClient.GetService(CaptureClientId, out var capturePointer));
        try
        {
            captureClient = (IAudioCaptureClientNative)ComWrappers.GetOrCreateObjectForComInstance(
                capturePointer,
                CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(capturePointer);
        }

        cancellation = new CancellationTokenSource();
        ThrowIfFailed(audioClient.Start());
        captureTask = Task.Run(() => CaptureLoop(cancellation.Token));
    }

    public void Stop()
    {
        cancellation?.Cancel();
        frameEvent.Set();
    }

    private void CaptureLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                frameEvent.WaitOne(150);
                if (cancellationToken.IsCancellationRequested) break;
                DrainPackets();
            }
        }
        catch (Exception exception)
        {
            Faulted?.Invoke(this, exception.Message);
        }
        finally
        {
            try { audioClient.Stop(); } catch { }
            try { audioClient.Reset(); } catch { }
        }
    }

    private void DrainPackets()
    {
        if (captureClient is null) return;
        ThrowIfFailed(captureClient.GetNextPacketSize(out var frames));
        while (frames > 0)
        {
            ThrowIfFailed(captureClient.GetBuffer(
                out var data,
                out var framesRead,
                out var flags,
                out _,
                out _));
            try
            {
                if (framesRead > 0)
                {
                    var samples = new float[framesRead * 2];
                    if ((flags & AudioClientBufferFlagsNative.Silent) == 0)
                    {
                        Marshal.Copy(data, samples, 0, samples.Length);
                    }
                    DataAvailable?.Invoke(this, samples);
                }
            }
            finally
            {
                ThrowIfFailed(captureClient.ReleaseBuffer(framesRead));
            }
            ThrowIfFailed(captureClient.GetNextPacketSize(out frames));
        }
    }

    private static async Task<IAudioClientNative> ActivateAsync(
        IntPtr activationParameters,
        CancellationToken cancellationToken)
    {
        var handler = new ActivateAudioInterfaceCompletionHandlerNative();
        var unknown = ComWrappers.GetOrCreateComInterfaceForObject(handler, CreateComInterfaceFlags.None);
        try
        {
            ThrowIfFailed(Marshal.QueryInterface(unknown, in CompletionHandlerId, out var handlerPointer));
            try
            {
                ThrowIfFailed(NativeMethods.ActivateAudioInterfaceAsync(
                    VirtualAudioDeviceProcessLoopback,
                    in AudioClientId,
                    activationParameters,
                    handlerPointer,
                    out var operationPointer));
                Marshal.Release(operationPointer);
            }
            finally
            {
                Marshal.Release(handlerPointer);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }

        return await handler.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ThrowIfFailed(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        if (captureTask is not null)
        {
            try { await captureTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        if ((object?)captureClient is ComObject captureObject) captureObject.FinalRelease();
        if ((object)audioClient is ComObject audioObject) audioObject.FinalRelease();
        cancellation?.Dispose();
        frameEvent.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariantBlobHeader
    {
        public ushort Vt;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public uint BlobSize;
        public IntPtr BlobData;
    }
}

[GeneratedComClass]
internal sealed partial class ActivateAudioInterfaceCompletionHandlerNative :
    IActivateAudioInterfaceCompletionHandlerNative,
    IAgileObjectNative
{
    private static readonly StrategyBasedComWrappers ComWrappers = new();
    private readonly TaskCompletionSource<IAudioClientNative> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<IAudioClientNative> Completion => completion.Task;

    public void ActivateCompleted(IntPtr activateOperationPointer)
    {
        var operation = (IActivateAudioInterfaceAsyncOperationNative)ComWrappers.GetOrCreateObjectForComInstance(
            activateOperationPointer,
            CreateObjectFlags.UniqueInstance);
        try
        {
            var callResult = operation.GetActivateResult(out var activationResult, out var clientPointer);
            if (callResult < 0)
            {
                completion.TrySetException(Marshal.GetExceptionForHR(callResult)!);
                return;
            }
            if (activationResult < 0)
            {
                completion.TrySetException(Marshal.GetExceptionForHR(activationResult)!);
                return;
            }
            try
            {
                var client = (IAudioClientNative)ComWrappers.GetOrCreateObjectForComInstance(
                    clientPointer,
                    CreateObjectFlags.UniqueInstance);
                completion.TrySetResult(client);
            }
            finally
            {
                Marshal.Release(clientPointer);
            }
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            if ((object)operation is ComObject operationObject) operationObject.FinalRelease();
        }
    }
}

[GeneratedComInterface, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IActivateAudioInterfaceCompletionHandlerNative
{
    void ActivateCompleted(IntPtr activateOperationPointer);
}

[GeneratedComInterface, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IAgileObjectNative;

[GeneratedComInterface, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IActivateAudioInterfaceAsyncOperationNative
{
    [PreserveSig]
    int GetActivateResult(out int activateResult, out IntPtr activatedInterface);
}

[GeneratedComInterface, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IAudioClientNative
{
    [PreserveSig]
    int Initialize(AudioClientShareModeNative shareMode, AudioClientStreamFlagsNative streamFlags,
        long bufferDuration, long periodicity, IntPtr format, in Guid sessionGuid);
    [PreserveSig] int GetBufferSize(out uint bufferSize);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out int currentPadding);
    [PreserveSig] int IsFormatSupported(AudioClientShareModeNative shareMode, IntPtr format, out IntPtr closestMatch);
    [PreserveSig] int GetMixFormat(out IntPtr format);
    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr eventHandle);
    [PreserveSig] int GetService(in Guid interfaceId, out IntPtr interfacePointer);
}

[GeneratedComInterface, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IAudioCaptureClientNative
{
    [PreserveSig]
    int GetBuffer(out IntPtr data, out int framesRead, out AudioClientBufferFlagsNative flags,
        out long devicePosition, out long qpcPosition);
    [PreserveSig] int ReleaseBuffer(int framesRead);
    [PreserveSig] int GetNextPacketSize(out int framesInNextPacket);
}

internal static partial class NativeMethods
{
    [LibraryImport("Mmdevapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int ActivateAudioInterfaceAsync(
        string deviceInterfacePath,
        in Guid interfaceId,
        IntPtr activationParameters,
        IntPtr completionHandler,
        out IntPtr activationOperation);
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientActivationParamsNative
{
    public AudioClientActivationTypeNative ActivationType;
    public AudioClientProcessLoopbackParamsNative ProcessLoopbackParams;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientProcessLoopbackParamsNative
{
    public uint TargetProcessId;
    public ProcessLoopbackModeNative ProcessLoopbackMode;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormatExNative
{
    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSecond;
    public uint AverageBytesPerSecond;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort ExtraSize;
}

internal enum AudioClientActivationTypeNative { Default, ProcessLoopback }
internal enum ProcessLoopbackModeNative { IncludeTargetProcessTree, ExcludeTargetProcessTree }
internal enum AudioClientShareModeNative { Shared, Exclusive }

[Flags]
internal enum AudioClientStreamFlagsNative : uint
{
    Loopback = 0x00020000,
    EventCallback = 0x00040000
}

[Flags]
internal enum AudioClientBufferFlagsNative
{
    None = 0,
    DataDiscontinuity = 1,
    Silent = 2,
    TimestampError = 4
}
