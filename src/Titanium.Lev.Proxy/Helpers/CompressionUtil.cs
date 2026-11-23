using Titanium.Lev.Proxy.Http;

namespace Titanium.Lev.Proxy.Compression;

internal static class CompressionUtil
{
    public static HttpCompression CompressionNameToEnum(string name)
    {
        if (KnownHeaders.ContentEncodingGzip.Equals(name))
            return HttpCompression.Gzip;

        if (KnownHeaders.ContentEncodingDeflate.Equals(name))
            return HttpCompression.Deflate;

        if (KnownHeaders.ContentEncodingBrotli.Equals(name))
            return HttpCompression.Brotli;

        if (KnownHeaders.ContentEncodingZstd.Equals(name))
            return HttpCompression.Zstd;

        return HttpCompression.Unsupported;
    }
}