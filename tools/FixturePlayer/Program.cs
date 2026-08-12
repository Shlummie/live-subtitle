using NAudio.Wave;

if (args.Length == 0 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: LiveSubtitle.FixturePlayer <audio-file> [delay-ms]");
    return 2;
}

var delayMs = args.Length > 1 && int.TryParse(args[1], out var parsedDelay)
    ? Math.Clamp(parsedDelay, 0, 60_000)
    : 0;
if (delayMs > 0) await Task.Delay(delayMs);

using var reader = new AudioFileReader(args[0]);
using var output = new WaveOutEvent { DesiredLatency = 80 };
var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
output.PlaybackStopped += (_, eventArgs) =>
{
    if (eventArgs.Exception is null) stopped.TrySetResult();
    else stopped.TrySetException(eventArgs.Exception);
};
output.Init(reader);
output.Play();
await stopped.Task.WaitAsync(TimeSpan.FromSeconds(90));
return 0;
