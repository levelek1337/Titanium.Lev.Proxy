using Microsoft.IO;

namespace Titanium.Lev.Proxy.Http;

internal static class MemoryStreamManager
{
    public static readonly RecyclableMemoryStreamManager Instance = new RecyclableMemoryStreamManager(
        new RecyclableMemoryStreamManager.Options
        {
            BlockSize = 128 * 1024,
            MaximumSmallPoolFreeBytes = 256 * 1024 * 1024,
            MaximumLargePoolFreeBytes = 512 * 1024 * 1024
        });
}
