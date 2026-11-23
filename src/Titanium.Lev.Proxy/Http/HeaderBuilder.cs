using System;
using System.Buffers;
using System.IO;
using System.Text;
using Titanium.Lev.Proxy.Models;
using Titanium.Lev.Proxy.Shared;

namespace Titanium.Lev.Proxy.Http;

internal class HeaderBuilder : IDisposable
{
    private readonly Microsoft.IO.RecyclableMemoryStream stream = MemoryStreamManager.Instance.GetStream();
    private byte[]? _buffer;

    public void Dispose()
    {
        if (_buffer != null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }
        stream.Dispose();
    }

    public void WriteRequestLine(string httpMethod, string httpUrl, Version version)
    {
        // "{httpMethod} {httpUrl} HTTP/{version.Major}.{version.Minor}";

        Write(httpMethod);
        Write(" ");
        Write(httpUrl);
        Write(" HTTP/");
        Write(version.Major.ToString());
        Write(".");
        Write(version.Minor.ToString());
        WriteLine();
    }

    public void WriteResponseLine(Version version, int statusCode, string statusDescription)
    {
        // "HTTP/{version.Major}.{version.Minor} {statusCode} {statusDescription}";

        Write("HTTP/");
        Write(version.Major.ToString());
        Write(".");
        Write(version.Minor.ToString());
        Write(" ");
        Write(statusCode.ToString());
        Write(" ");
        Write(statusDescription);
        WriteLine();
    }

    public void WriteHeaders(HeaderCollection headers, bool sendProxyAuthorization = true,
        string? upstreamProxyUserName = null, string? upstreamProxyPassword = null)
    {
        if (upstreamProxyUserName != null && upstreamProxyPassword != null)
        {
            WriteHeader(HttpHeader.ProxyConnectionKeepAlive);
            WriteHeader(HttpHeader.GetProxyAuthorizationHeader(upstreamProxyUserName, upstreamProxyPassword));
        }

        foreach (var header in headers)
            if (sendProxyAuthorization || !KnownHeaders.ProxyAuthorization.Equals(header.Name))
                WriteHeader(header);

        WriteLine();
    }

    public void WriteHeader(HttpHeader header)
    {
        Write(header.Name);
        Write(": ");
        Write(header.Value);
        WriteLine();
    }

    public void WriteLine()
    {
        var data = ProxyConstants.NewLineBytes;
        stream.Write(data, 0, data.Length);
    }

    public void Write(string str)
    {
        var encoding = HttpHeader.Encoding;

#if NET6_0_OR_GREATER
        var maxBytes = encoding.GetMaxByteCount(str.Length);
        if (_buffer == null || _buffer.Length < maxBytes)
        {
            if (_buffer != null) ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(maxBytes, 4096));
        }
        var span = new Span<byte>(_buffer);

        int bytes = encoding.GetBytes(str.AsSpan(), span);

        stream.Write(span.Slice(0, bytes));
#else
        var maxBytes = encoding.GetMaxByteCount(str.Length);
        if (_buffer == null || _buffer.Length < maxBytes)
        {
            if (_buffer != null) ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(maxBytes, 4096));
        }
        int bytes = encoding.GetBytes(str, 0, str.Length, _buffer, 0);
        stream.Write(_buffer, 0, bytes);
#endif
    }

    public ArraySegment<byte> GetBuffer()
    {
        stream.TryGetBuffer(out var buffer);
        return buffer;
    }

    public string GetString(Encoding encoding)
    {
        stream.TryGetBuffer(out var buffer);
        return encoding.GetString(buffer.Array, buffer.Offset, buffer.Count);
    }
}
