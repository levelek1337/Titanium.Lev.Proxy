using System;
using Titanium.Lev.Proxy.Models;

namespace Titanium.Lev.Proxy.Extensions;

internal static class HttpHeaderExtensions
{
    internal static string GetString(this ByteString str)
    {
        return GetString(str.Span);
    }

    internal static string GetString(this ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) return string.Empty;
#if NET6_0_OR_GREATER
        return HttpHeader.Encoding.GetString(bytes);
#else
        unsafe
        {
            fixed (byte* bp = bytes)
            {
                return HttpHeader.Encoding.GetString(bp, bytes.Length);
            }
        }
#endif
    }

    internal static ByteString GetByteString(this string str)
    {
        return HttpHeader.Encoding.GetBytes(str);
    }
}