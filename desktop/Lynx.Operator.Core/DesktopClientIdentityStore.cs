using System.Security.Cryptography;

namespace Lynx.Operator.Core;

public sealed class DesktopClientIdentityStore
{
    private readonly string _path;

    public DesktopClientIdentityStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sentinel Systems",
            "Lynx",
            "operator-client-id.txt");
    }

    public long GetOrCreate()
    {
        if (File.Exists(_path) &&
            long.TryParse(File.ReadAllText(_path).Trim(), out var existing) &&
            existing > 0)
        {
            return existing;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        long generated;
        do
        {
            generated = PositiveIdGenerator.Next();
        } while (generated <= 0);

        File.WriteAllText(_path, generated.ToString());
        return generated;
    }
}
