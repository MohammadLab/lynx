using Lynx.Operator.Core;

namespace Lynx.Tests;

public sealed class DesktopClientIdentityStoreTests
{
    [Fact]
    public void IdentityIsPositiveAndStableAcrossInstances()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "lynx-tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "id.txt");

        try
        {
            var first = new DesktopClientIdentityStore(path).GetOrCreate();
            var second = new DesktopClientIdentityStore(path).GetOrCreate();

            Assert.True(first > 0);
            Assert.Equal(first, second);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}