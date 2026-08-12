using System.Diagnostics;
using System.IO.Pipes;
using System.Threading.Channels;
using LiveSubtitle.App.Audio;
using LiveSubtitle.Core;

namespace LiveSubtitle.App.Engine;

public sealed class EngineClient : IAsyncDisposable
{
    private readonly Channel<EngineMessage> outgoing = Channel.CreateBounded<EngineMessage>(new BoundedChannelOptions(64)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private CancellationTokenSource? cancellation;
    private NamedPipeServerStream? pipe;
    private PipeMessageCodec? codec;
    private Process? child;
    private Task? writeTask;
    private Task? readTask;
    private TaskCompletionSource<string>? ready;
    private int stopping;

    public event EventHandler<CaptionEvent>? CaptionReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? Faulted;

    public bool IsConnected => pipe?.IsConnected == true && child?.HasExited == false;
    internal int? ChildProcessId => child?.Id;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (cancellation is not null) return;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pipeName = $"LiveSubtitle-{Environment.ProcessId}-{Guid.NewGuid():N}";
        pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            64 * 1024,
            64 * 1024);

        var executable = Environment.ProcessPath
                         ?? throw new InvalidOperationException("Cannot locate the LiveSubtitle executable.");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add("--engine");
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
        child = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the local recognition engine.");
        await pipe.WaitForConnectionAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(15), cancellation.Token)
            .ConfigureAwait(false);
        codec = new PipeMessageCodec(pipe);
        writeTask = Task.Run(() => WriteLoopAsync(cancellation.Token));
        readTask = Task.Run(() => ReadLoopAsync(cancellation.Token));
        StatusChanged?.Invoke(this, "Recognition engine connected");
    }

    public async Task<string> LoadModelAsync(
        string sessionId,
        string modelPath,
        string vadModelPath,
        RecognitionProfile profile,
        CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await QueueReliableAsync(new EngineMessage
        {
            Kind = EngineMessageKind.LoadModel,
            RequestId = sessionId,
            ModelPath = modelPath,
            VadModelPath = vadModelPath,
            Backend = profile.Backend,
            Recognition = profile
        }, cancellationToken).ConfigureAwait(false);
        return await ready.Task.WaitAsync(TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
    }

    public void SendAudio(AudioChunk chunk)
    {
        outgoing.Writer.TryWrite(new EngineMessage
        {
            Kind = EngineMessageKind.AudioFrame,
            SourceId = chunk.SourceId,
            SourceLabel = chunk.SourceLabel,
            SourceColorHex = chunk.SourceColorHex,
            Samples = chunk.Samples,
            CapturedAtUtc = chunk.CapturedAtUtc
        });
    }

    private async Task QueueReliableAsync(EngineMessage message, CancellationToken cancellationToken)
    {
        await outgoing.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in outgoing.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await codec!.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Faulted?.Invoke(this, $"Recognition engine write failed: {exception.Message}");
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await codec!.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    if (Volatile.Read(ref stopping) == 0)
                    {
                        Faulted?.Invoke(this, "Recognition engine exited unexpectedly.");
                    }
                    break;
                }
                switch (message.Kind)
                {
                    case EngineMessageKind.Ready:
                        ready?.TrySetResult(message.Detail ?? "Ready");
                        StatusChanged?.Invoke(this, message.Detail ?? "Recognition engine ready");
                        break;
                    case EngineMessageKind.Caption when message.Caption is not null:
                        CaptionReceived?.Invoke(this, message.Caption);
                        break;
                    case EngineMessageKind.Metrics:
                        StatusChanged?.Invoke(this, message.Detail ?? "Engine metrics updated");
                        break;
                    case EngineMessageKind.Error:
                        var detail = message.Detail ?? "The recognition engine reported an error.";
                        ready?.TrySetException(new InvalidOperationException(detail));
                        Faulted?.Invoke(this, detail);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ready?.TrySetException(exception);
            Faulted?.Invoke(this, $"Recognition engine stopped: {exception.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref stopping, 1);
        var activeCancellation = cancellation;
        cancellation = null;
        if (activeCancellation is not null && codec is not null)
        {
            try
            {
                await codec.WriteAsync(new EngineMessage { Kind = EngineMessageKind.Stop }, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch { }
        }
        activeCancellation?.Cancel();
        outgoing.Writer.TryComplete();
        if (writeTask is not null) { try { await writeTask.ConfigureAwait(false); } catch { } }
        if (readTask is not null) { try { await readTask.ConfigureAwait(false); } catch { } }
        if (child is { HasExited: false })
        {
            try
            {
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                child.Kill(entireProcessTree: true);
            }
        }
        child?.Dispose();
        pipe?.Dispose();
        activeCancellation?.Dispose();
        child = null;
        pipe = null;
        codec = null;
    }

    internal bool TerminateChildForDiagnostics()
    {
        if (child is not { HasExited: false }) return false;
        child.Kill(entireProcessTree: true);
        return true;
    }
}
