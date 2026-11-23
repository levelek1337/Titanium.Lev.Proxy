using System;
using System.ComponentModel;
using Titanium.Lev.Proxy.Exceptions;
using Titanium.Lev.Proxy.Extensions;
using Titanium.Lev.Proxy.Models;

namespace Titanium.Lev.Proxy.Http;

/// <summary>
///     Http(s) response object
/// </summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public class Response : RequestResponseBase
{
    /// <summary>
    ///     Constructor.
    /// </summary>
    public Response()
    {
    }

    /// <summary>
    ///     Constructor.
    /// </summary>
    public Response(Microsoft.IO.RecyclableMemoryStream bodyStream)
    {
        BodyStream = bodyStream;
    }

    /// <summary>
    ///     Response Status Code.
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    ///     Response Status description.
    /// </summary>
    public string StatusDescription { get; set; } = string.Empty;

    internal string RequestMethod { get; set; }

    /// <summary>
    ///     Has response body?
    /// </summary>
    public override bool HasBody
    {
        get
        {
            if (RequestMethod == "HEAD") return false;

            var contentLength = ContentLength;

            // If content length is set to 0 the response has no body
            if (contentLength == 0) return false;

            // Has body only if response is chunked or content length >0
            // If none are true then check if connection:close header exist, if so write response until server or client terminates the connection
            if (IsChunked || contentLength > 0 || !KeepAlive) return true;

            if (ContentLength == -1 && HttpVersion == HttpHeader.Version20) return true;

            // has response if connection:keep-alive header exist and when version is http/1.0
            // Because in Http 1.0 server can return a response without content-length (expectation being client would read until end of stream)
            if (KeepAlive && HttpVersion == HttpHeader.Version10) return true;

            return false;
        }
    }

    /// <summary>
    ///     Keep the connection alive?
    /// </summary>
    public bool KeepAlive
    {
        get
        {
            var headerValue = Headers.GetHeaderValueOrNull(KnownHeaders.Connection);

            if (headerValue != null)
                if (headerValue.EqualsIgnoreCase(KnownHeaders.ConnectionClose.String))
                    return false;

            return true;
        }
    }

    /// <summary>
    ///     Gets the header text.
    /// </summary>
    public override string HeaderText
    {
        get
        {
            using (var headerBuilder = new HeaderBuilder())
            {
            headerBuilder.WriteResponseLine(HttpVersion, StatusCode, StatusDescription);
            headerBuilder.WriteHeaders(Headers);
            return headerBuilder.GetString(HttpHeader.Encoding);
            }
        }
    }

    internal override void EnsureBodyAvailable(bool throwWhenNotReadYet = true)
    {
        if (BodyInternal != null) return;

        if (!HasBody) throw new BodyNotFoundException("Response don't have a body.");

        if (!IsBodyRead && throwWhenNotReadYet)
            throw new Exception("Response body is not read yet. " +
                                "Use SessionEventArgs.GetResponseBody() or SessionEventArgs.GetResponseBodyAsString() " +
                                "method to read the response body.");
    }

    internal static void ParseResponseLine(System.ReadOnlySpan<byte> httpStatus, out Version version, out int statusCode,
        out string statusDescription)
    {
        var firstSpace = httpStatus.IndexOf((byte)' ');
        if (firstSpace == -1) throw new Exception("Invalid HTTP status line");

        var httpVersion = httpStatus.Slice(0, firstSpace);
        var versionStr = System.Text.Encoding.ASCII.GetString(httpVersion.ToArray());

        version = HttpHeader.Version11;
        if (versionStr.Equals("HTTP/1.0", System.StringComparison.OrdinalIgnoreCase)) version = HttpHeader.Version10;

        var secondSpaceRaw = httpStatus.Slice(firstSpace + 1).IndexOf((byte)' ');
        if (secondSpaceRaw != -1)
        {
            var secondSpace = firstSpace + 1 + secondSpaceRaw;
            var statusCodeSpan = httpStatus.Slice(firstSpace + 1, secondSpace - firstSpace - 1);
            var statusCodeStr = System.Text.Encoding.ASCII.GetString(statusCodeSpan.ToArray());
            statusCode = int.Parse(statusCodeStr);
            var descSpan = httpStatus.Slice(secondSpace + 1);
            statusDescription = System.Text.Encoding.ASCII.GetString(descSpan.ToArray());
        }
        else
        {
            var statusCodeSpan = httpStatus.Slice(firstSpace + 1);
            var statusCodeStr = System.Text.Encoding.ASCII.GetString(statusCodeSpan.ToArray());
            statusCode = int.Parse(statusCodeStr);
            statusDescription = string.Empty;
        }
    }
}
