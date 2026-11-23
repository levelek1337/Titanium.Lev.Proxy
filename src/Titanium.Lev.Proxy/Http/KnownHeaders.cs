using System;
namespace Titanium.Lev.Proxy.Http;

/// <summary>
///     Well known http headers.
/// </summary>
public static class KnownHeaders
{
    // Both
    public static KnownHeader Connection = "Connection";
    public static KnownHeader ConnectionClose = "close";
    public static KnownHeader ConnectionKeepAlive = "keep-alive";

    public static KnownHeader ContentLength = "Content-Length";
    public static KnownHeader ContentLengthHttp2 = "content-length";

    public static KnownHeader ContentType = "Content-Type";
    public static KnownHeader ContentTypeCharset = "charset";
    public static KnownHeader ContentTypeBoundary = "boundary";

    public static KnownHeader Upgrade = "Upgrade";
    public static KnownHeader UpgradeWebsocket = "websocket";

    // Request headers
    public static KnownHeader AcceptEncoding = "Accept-Encoding";

    public static KnownHeader Authorization = "Authorization";

    public static KnownHeader Expect = "Expect";
    public static KnownHeader Expect100Continue = "100-continue";

    public static KnownHeader Host = "Host";

    public static KnownHeader ProxyAuthorization = "Proxy-Authorization";
    public static KnownHeader ProxyAuthorizationBasic = "basic";

    public static KnownHeader ProxyConnection = "Proxy-Connection";
    public static KnownHeader ProxyConnectionClose = "close";

    // Response headers
    public static KnownHeader ContentEncoding = "Content-Encoding";
    public static KnownHeader ContentEncodingDeflate = "deflate";
    public static KnownHeader ContentEncodingGzip = "gzip";
    public static KnownHeader ContentEncodingBrotli = "br";
    public static KnownHeader ContentEncodingZstd = "zstd";

    public static KnownHeader Location = "Location";

    public static KnownHeader ProxyAuthenticate = "Proxy-Authenticate";

    public static KnownHeader TransferEncoding = "Transfer-Encoding";
    public static KnownHeader TransferEncodingChunked = "chunked";
    private static bool EqualsIgnoreCase(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            var ca = a[i];
            var cb = b[i];
            if (ca >= 'A' && ca <= 'Z') ca = (byte)(ca + 32);
            if (cb >= 'A' && cb <= 'Z') cb = (byte)(cb + 32);
            if (ca != cb) return false;
        }
        return true;
    }

    public static KnownHeader? GetKnownHeader(ReadOnlySpan<byte> span)
    {
        if (span.IsEmpty) return null;
        
        switch (span.Length)
        {
            case 2:
                if (EqualsIgnoreCase(span, ContentEncodingBrotli.String8.Span)) return ContentEncodingBrotli;
                break;
            case 4:
                if (EqualsIgnoreCase(span, Host.String8.Span)) return Host;
                if (EqualsIgnoreCase(span, ContentEncodingZstd.String8.Span)) return ContentEncodingZstd;
                if (EqualsIgnoreCase(span, ContentEncodingGzip.String8.Span)) return ContentEncodingGzip;
                break;
            case 5:
                if (EqualsIgnoreCase(span, ConnectionClose.String8.Span)) return ConnectionClose;
                if (EqualsIgnoreCase(span, ProxyAuthorizationBasic.String8.Span)) return ProxyAuthorizationBasic;
                break;
            case 6:
                if (EqualsIgnoreCase(span, Expect.String8.Span)) return Expect;
                break;
            case 7:
                if (EqualsIgnoreCase(span, Upgrade.String8.Span)) return Upgrade;
                if (EqualsIgnoreCase(span, ContentEncodingDeflate.String8.Span)) return ContentEncodingDeflate;
                if (EqualsIgnoreCase(span, TransferEncodingChunked.String8.Span)) return TransferEncodingChunked;
                if (EqualsIgnoreCase(span, ContentTypeCharset.String8.Span)) return ContentTypeCharset;
                break;
            case 8:
                if (EqualsIgnoreCase(span, Location.String8.Span)) return Location;
                if (EqualsIgnoreCase(span, ContentTypeBoundary.String8.Span)) return ContentTypeBoundary;
                break;
            case 9:
                if (EqualsIgnoreCase(span, UpgradeWebsocket.String8.Span)) return UpgradeWebsocket;
                break;
            case 10:
                if (EqualsIgnoreCase(span, Connection.String8.Span)) return Connection;
                if (EqualsIgnoreCase(span, ConnectionKeepAlive.String8.Span)) return ConnectionKeepAlive;
                break;
            case 12:
                if (EqualsIgnoreCase(span, ContentType.String8.Span)) return ContentType;
                if (EqualsIgnoreCase(span, Expect100Continue.String8.Span)) return Expect100Continue;
                break;
            case 13:
                if (EqualsIgnoreCase(span, Authorization.String8.Span)) return Authorization;
                break;
            case 14:
                if (EqualsIgnoreCase(span, ContentLength.String8.Span)) return ContentLength;
                if (EqualsIgnoreCase(span, ContentLengthHttp2.String8.Span)) return ContentLengthHttp2;
                break;
            case 15:
                if (EqualsIgnoreCase(span, AcceptEncoding.String8.Span)) return AcceptEncoding;
                break;
            case 16:
                if (EqualsIgnoreCase(span, ProxyConnection.String8.Span)) return ProxyConnection;
                if (EqualsIgnoreCase(span, ContentEncoding.String8.Span)) return ContentEncoding;
                break;
            case 17:
                if (EqualsIgnoreCase(span, TransferEncoding.String8.Span)) return TransferEncoding;
                break;
            case 18:
                if (EqualsIgnoreCase(span, ProxyAuthenticate.String8.Span)) return ProxyAuthenticate;
                break;
            case 19:
                if (EqualsIgnoreCase(span, ProxyAuthorization.String8.Span)) return ProxyAuthorization;
                break;
        }

        return null;
    }

}
