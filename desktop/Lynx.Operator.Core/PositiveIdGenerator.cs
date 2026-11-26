using System.Security.Cryptography;

namespace Lynx.Operator.Core;

internal static class PositiveIdGenerator
{
    public static long Next()
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        long value;

        do
        {
            RandomNumberGenerator.Fill(bytes);
            value = BitConverter.ToInt64(bytes) & long.MaxValue;
        } while (value <= 0);

        return value;
    }
}
