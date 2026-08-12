namespace LiveSubtitle.App.Engine;

internal sealed record RecognitionJob(
    string SourceId,
    string SourceLabel,
    string SourceColorHex,
    float[] Samples,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    long Revision,
    bool IsFinal,
    bool EndsUtterance);

internal sealed class RecognitionScheduler
{
    private readonly object gate = new();
    private readonly Queue<RecognitionJob> finals = new();
    private readonly Dictionary<string, RecognitionJob> drafts = new();
    private readonly SemaphoreSlim signal = new(0);

    public void Enqueue(RecognitionJob job)
    {
        var shouldSignal = true;
        lock (gate)
        {
            if (job.IsFinal)
            {
                finals.Enqueue(job);
            }
            else
            {
                shouldSignal = !drafts.ContainsKey(job.SourceId);
                drafts[job.SourceId] = job;
            }
        }
        if (shouldSignal) signal.Release();
    }

    public async Task<RecognitionJob> DequeueAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                RecognitionJob? oldestFinal = finals.Count > 0 ? finals.Peek() : null;
                RecognitionJob? oldestDraft = drafts.Values.OrderBy(job => job.CapturedAtUtc).FirstOrDefault();
                if (oldestFinal is null && oldestDraft is null) continue;
                if (oldestDraft is null || oldestFinal is not null && oldestFinal.CapturedAtUtc <= oldestDraft.CapturedAtUtc)
                {
                    return finals.Dequeue();
                }
                drafts.Remove(oldestDraft.SourceId);
                return oldestDraft;
            }
        }
    }
}
