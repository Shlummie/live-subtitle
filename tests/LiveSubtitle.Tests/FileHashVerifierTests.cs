using System.Text;
using LiveSubtitle.Core;

namespace LiveSubtitle.Tests;

public sealed class FileHashVerifierTests
{
    [Fact]
    public async Task ComputesKnownSha256()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("LiveSubtitle"));
            var actual = await FileHashVerifier.ComputeSha256Async(path);
            Assert.Equal("f274f6d56b26cb9821440537a44aa1734f5e230b6232c9bd973dc3c013fa5181", actual);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
