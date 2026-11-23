using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Titanium.Lev.Proxy.Compression;
using Titanium.Lev.Proxy.Extensions;
using Titanium.Lev.Proxy.Helpers;
using Titanium.Lev.Proxy.Models;

namespace Titanium.Lev.Proxy.Http;

/// <summary>
///     Abstract base class for similar objects shared by both request and response objects.
/// </summary>
public abstract class RequestResponseBase
{
    /// <summary>
    ///     Cached body as string.
    /// </summary>
    private string? bodyString;

    internal Task? Http2BeforeHandlerTask;

    internal Microsoft.IO.RecyclableMemoryStream? Http2BodyData;

    internal bool Http2IgnoreBodyFrames;

    /// <summary>
    ///     Priority used only in HTTP/2
    /// </summary>
    internal long? Priority;

    internal TaskCompletionSource<bool>? ReadHttp2BeforeHandlerTaskCompletionSource;

    internal TaskCompletionSource<bool>? ReadHttp2BodyTaskCompletionSource;

    /// <summary>
    ///     Is the body externally owned? If true, it won't be disposed by Titanium Web Proxy.
    /// </summary>
    public bool ExternallyOwned { get; set; }

    /// <summary>
    ///     Cached body content as stream.
    /// </summary>
    protected Microsoft.IO.RecyclableMemoryStream? BodyInternal { get; private set; }

    /// <summary>
    ///     Store whether the original request/response has body or not, since the user may change the parameters.
    ///     We need this detail to syphon out attached tcp connection for reuse.
    /// </summary>
    internal bool OriginalHasBody { get; set; }

    /// <summary>
    ///     Store original content-length, since the user setting the body may change the parameters.
    ///     We need this detail to tcp syphon out attached connection for reuse.
    /// </summary>
    internal long OriginalContentLength { get; set; }

    /// <summary>
    ///     Store whether the original request/response was a chunked body, since the user may change the parameters.
    ///     We need this detail to syphon out attached tcp connection for reuse.
    /// </summary>
    internal bool OriginalIsChunked { get; set; }

    /// <summary>
    ///     Store whether the original request/response content-encoding, since the user may change the parameters.
    ///     We need this detail to syphon out attached tcp connection for reuse.
    /// </summary>
    internal string? OriginalContentEncoding { get; set; }

    /// <summary>
    ///     Keeps the body data after the session is finished.
    /// </summary>
    public bool KeepBody { get; set; }

    /// <summary>
    ///     Http Version.
    /// </summary>
    public Version HttpVersion { get; set; } = HttpHeader.VersionUnknown;

    /// <summary>
    ///     Collection of all headers.
    /// </summary>
    public HeaderCollection Headers { get; } = new();

    /// <summary>
    ///     Length of the body.
    /// </summary>
    public long ContentLength
    {
        get
        {
            var headerValue = Headers.GetHeaderValueOrNull(KnownHeaders.ContentLength);

            if (headerValue == null) return -1;

            if (long.TryParse(headerValue, out var contentLen) && contentLen >= 0) return contentLen;

            return -1;
        }

        set
        {
            if (value >= 0)
            {
                Headers.SetOrAddHeaderValue(
                    HttpVersion >= HttpHeader.Version20
                        ? KnownHeaders.ContentLengthHttp2
                        : KnownHeaders.ContentLength, value.ToString());
                IsChunked = false;
                ContentLengthSetManually = true;
            }
            else
            {
                Headers.RemoveHeader(KnownHeaders.ContentLength);
                ContentLengthSetManually = false;
            }
        }
    }

    /// <summary>
    ///     True if the Content-Length was set manually by the user.
    /// </summary>
    public bool ContentLengthSetManually { get; set; }

    /// <summary>
    ///     Content encoding for this request/response.
    /// </summary>
    public string? ContentEncoding => Headers.GetHeaderValueOrNull(KnownHeaders.ContentEncoding)?.Trim();

    /// <summary>
    ///     Encoding for this request/response.
    /// </summary>
    public Encoding Encoding => HttpHelper.GetEncodingFromContentType(ContentType);

    /// <summary>
    ///     Content-type of the request/response.
    /// </summary>
    public string? ContentType
    {
        get => Headers.GetHeaderValueOrNull(KnownHeaders.ContentType);
        set => Headers.SetOrAddHeaderValue(KnownHeaders.ContentType, value);
    }

    /// <summary>
    ///     Is body send as chunked bytes.
    /// </summary>
    public bool IsChunked
    {
        get
        {
            var headerValue = Headers.GetHeaderValueOrNull(KnownHeaders.TransferEncoding);
            return headerValue != null && headerValue.ContainsIgnoreCase(KnownHeaders.TransferEncodingChunked.String);
        }

        set
        {
            if (value)
            {
                Headers.SetOrAddHeaderValue(KnownHeaders.TransferEncoding, KnownHeaders.TransferEncodingChunked);
                ContentLength = -1;
            }
            else
            {
                Headers.RemoveHeader(KnownHeaders.TransferEncoding);
            }
        }
    }

    /// <summary>
    ///     The header text.
    /// </summary>
    public abstract string HeaderText { get; }

    /// <summary>
    ///     Body as stream
    /// </summary>
    [Browsable(false)]
    public Microsoft.IO.RecyclableMemoryStream BodyStream
    {
        get
        {
            EnsureBodyAvailable();
            return BodyInternal!;
        }

        internal set
        {
            if (BodyInternal != null && BodyInternal != value && !ExternallyOwned)
            {
                BodyInternal.Dispose();
            }

            BodyInternal = value;
            bodyString = null;

            // Only update Content-Length when user explicitly sets a body stream.
            // Do NOT touch Content-Length for pass-through (null) — the original header stays intact.
            if (value != null)
            {
                UpdateContentLength();
            }
        }
    }

    /// <summary>
    ///     Has the request/response body?
    /// </summary>
    public abstract bool HasBody { get; }

    /// <summary>
    ///     Body as string.
    ///     Use the encoding specified to decode the body data to string
    /// </summary>
    [Browsable(false)]
    public string BodyString
    {
        get
        {
            if (bodyString != null) return bodyString;
            var stream = BodyStream;
            var pos = stream.Position;
            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding, false, 1024, true);
            bodyString = reader.ReadToEnd();
            stream.Position = pos;
            return bodyString;
        }
    }

    /// <summary>
    ///     Was the body read by user?
    /// </summary>
    public bool IsBodyRead { get; internal set; }
    public bool IsBodyCompressed { get; internal set; }

    /// <summary>
    ///     Is the request/response no more modifiable by user (user callbacks complete?)
    ///     Also if user set this as a custom response then this should be true.
    /// </summary>
    internal bool Locked { get; set; }

    internal bool BodyAvailable => BodyInternal != null;

    internal bool IsBodyReceived { get; set; }

    internal bool IsBodySent { get; set; }

    internal abstract void EnsureBodyAvailable(bool throwWhenNotReadYet = true);

    internal async Task<Microsoft.IO.RecyclableMemoryStream> GetCompressedBodyAsync(HttpCompression encodingType, Microsoft.IO.RecyclableMemoryStream bodyStream)
    {
        var ms = MemoryStreamManager.Instance.GetStream();
        using (var zip = CompressionFactory.Create(encodingType, ms))
        {
            var pos = bodyStream.Position;
            bodyStream.Position = 0;
            await bodyStream.CopyToAsync(zip).ConfigureAwait(false);
            bodyStream.Position = pos;
        }

        return ms;
    }

    internal async Task<Microsoft.IO.RecyclableMemoryStream?> CompressBodyAndUpdateContentLengthAsync()
    {
        if (!IsBodyRead && BodyInternal == null) return null;

        var isChunked = IsChunked;
        var contentEncoding = ContentEncoding;

        if (HasBody)
        {
            var body = BodyStream;
            if (contentEncoding != null && !IsBodyCompressed)
            {
                body = await GetCompressedBodyAsync(CompressionUtil.CompressionNameToEnum(contentEncoding), body).ConfigureAwait(false);

                if (isChunked == false)
                {
                    if (!ContentLengthSetManually) ContentLength = body.Length;
                }
                else
                {
                    if (!ContentLengthSetManually) ContentLength = -1;
                }
            }
            else
            {
                // If there is no compression and body was explicitly provided,
                // ensure Content-Length matches the actual body length and chunked is false unless explicitly requested.
                if (isChunked == false)
                {
                    if (!ContentLengthSetManually) ContentLength = body.Length;
                }
                else
                {
                    if (!ContentLengthSetManually) ContentLength = -1;
                }
            }

            body.Position = 0;
            return body;
        }

        if (!ContentLengthSetManually) ContentLength = 0;
        return null;
    }

    internal void UpdateContentLength()
    {
        if (!ContentLengthSetManually)
        {
            ContentLength = IsChunked ? -1 : BodyInternal?.Length ?? 0;
            ContentLengthSetManually = false;
        }
    }

    /// <summary>
    ///     Set values for original headers using current headers.
    /// </summary>
    internal void SetOriginalHeaders()
    {
        OriginalHasBody = HasBody;
        OriginalContentLength = ContentLength;
        OriginalIsChunked = IsChunked;
        OriginalContentEncoding = ContentEncoding;
    }

    /// <summary>
    ///     Copy original header values.
    /// </summary>
    /// <param name="requestResponseBase"></param>
    internal void SetOriginalHeaders(RequestResponseBase requestResponseBase)
    {
        OriginalHasBody = requestResponseBase.OriginalHasBody;
        OriginalContentLength = requestResponseBase.OriginalContentLength;
        OriginalIsChunked = requestResponseBase.OriginalIsChunked;
        OriginalContentEncoding = requestResponseBase.OriginalContentEncoding;
    }

    /// <summary>
    ///     Finish the session
    /// </summary>
    internal void FinishSession()
    {
        if (!KeepBody)
        {
            if (BodyInternal != null && !ExternallyOwned)
            {
                BodyInternal.Dispose();
            }
            BodyInternal = null;
            bodyString = null;
        }
    }

    public override string ToString()
    {
        return HeaderText;
    }
}
