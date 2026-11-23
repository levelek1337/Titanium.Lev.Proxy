namespace Titanium.Lev.Proxy.Compression;

internal enum HttpCompression
{
    Unsupported = 0,
    Gzip = 1,
    Deflate = 2,
    Brotli = 3,
    Zstd = 4
}