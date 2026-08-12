using System.Security.Cryptography;

namespace LiveSubtitle.Core;

public static class FileHashVerifier
{
    public static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    public static async Task<bool> MatchesSha256Async(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path) || expectedSha256.Length != 64)
        {
            return false;
        }

        var actual = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actual),
            Convert.FromHexString(expectedSha256));
    }
}
