using System;
using System.ComponentModel;
using Titanium.Lev.Proxy.Exceptions;
using Titanium.Lev.Proxy.Extensions;
using Titanium.Lev.Proxy.Models;

namespace Titanium.Lev.Proxy.Http;

/// <summary>
///     Http(s) request object
/// </summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public class Request : RequestResponseBase
{
    private ByteString requestUriString8;

    /// <summary>
    ///     Request Method.
    /// </summary>
    public string Method { get; set; }

    /// <summary>
    ///     Is Https?
    /// </summary>
    public bool IsHttps { get; internal set; }

    internal ByteString RequestUriString8
    {
        get => requestUriString8;
        set
        {
            requestUriString8 = value;
            var scheme = UriExtensions.GetScheme(value);
            if (scheme.Length > 0) IsHttps = scheme.Equals(ProxyServer.UriSchemeHttps8);
        }
    }

    internal ByteString Authority { get; set; }

    /// <summary>
    ///     Request HTTP Uri.
    /// </summary>
    public Uri RequestUri
    {
        get
        {
            var url = Url;
            try
            {
                return new Uri(url);
            }
            catch (Exception ex)
            {
                throw new Exception($"Invalid URI: '{url}'", ex);
            }
        }
        set => Url = value.OriginalString;
    }

    /// <summary>
    ///     The request url as it is in the HTTP header
    /// </summary>
    public string Url
    {
        get
        {
            var url = RequestUriString8.GetString();
            if (UriExtensions.GetScheme(RequestUriString8).Length == 0)
            {
                var hostAndPath = Host ?? Authority.GetString();

                if (url.StartsWith("/"))
                {
                    hostAndPath += url;
                }

                url = string.Concat(IsHttps ? "https://" : "http://", hostAndPath);
            }

            return url;
        }
        set => RequestUriString = value;
    }

    /// <summary>
    ///     The request uri as it is in the HTTP header
    /// </summary>
    public string RequestUriString
    {
        get => RequestUriString8.GetString();
        set
        {
            RequestUriString8 = (ByteString)value;

            var scheme = UriExtensions.GetScheme(RequestUriString8);
            if (scheme.Length > 0 && Host != null)
            {
                var uri = new Uri(value);
                Host = uri.Authority;
                Authority = ByteString.Empty;
            }
        }
    }

    /// <summary>
    ///     Has request body?
    /// </summary>
    public override bool HasBody
    {
        get
        {
            var contentLength = ContentLength;

            // If content length is set to 0 the request has no body
            if (contentLength == 0) return false;

            // Has body only if request is chunked or content length >0
            if (IsChunked || contentLength > 0) return true;

            // has body if POST and when version is http/1.0
            if (Method == "POST" && HttpVersion == HttpHeader.Version10) return true;

            return false;
        }
    }

    /// <summary>
    ///     Http hostname header value if exists.
    ///     Note: Changing this does NOT change host in RequestUri.
    ///     Users can set new RequestUri separately.
    /// </summary>
    public string? Host
    {
        get => Headers.GetHeaderValueOrNull(KnownHeaders.Host);
        set => Headers.SetOrAddHeaderValue(KnownHeaders.Host, value);
    }

    /// <summary>
    ///     Does this request has a 100-continue header?
    /// </summary>
    public bool ExpectContinue
    {
        get
        {
            var headerValue = Headers.GetHeaderValueOrNull(KnownHeaders.Expect);
            return KnownHeaders.Expect100Continue.Equals(headerValue);
        }
    }

    /// <summary>
    ///     Does this request contain multipart/form-data?
    /// </summary>
    public bool IsMultipartFormData => ContentType?.StartsWith("multipart/form-data") == true;

    /// <summary>
    ///     Cancels the client HTTP request without sending to server.
    ///     This should be set when API user responds with custom response.
    /// </summary>
    internal bool CancelRequest { get; set; }

    /// <summary>
    ///     Does this request has an upgrade to websocket header?
    /// </summary>
    public bool UpgradeToWebSocket
    {
        get
        {
            var headerValue = Headers.GetHeaderValueOrNull(KnownHeaders.Upgrade);

            if (headerValue == null) return false;

            return headerValue.EqualsIgnoreCase(KnownHeaders.UpgradeWebsocket.String);
        }
    }

    /// <summary>
    ///     Did server respond positively for 100 continue request?
    /// </summary>
    public bool ExpectationSucceeded { get; internal set; }

    /// <summary>
    ///     Did server respond negatively for 100 continue request?
    /// </summary>
    public bool ExpectationFailed { get; internal set; }

    /// <summary>
    ///     Gets the header text.
    /// </summary>
    public override string HeaderText
    {
        get
        {
            using (var headerBuilder = new HeaderBuilder())
            {
            headerBuilder.WriteRequestLine(Method, RequestUriString, HttpVersion);
            headerBuilder.WriteHeaders(Headers);
            return headerBuilder.GetString(HttpHeader.Encoding);
            }
        }
    }

    internal override void EnsureBodyAvailable(bool throwWhenNotReadYet = true)
    {
        if (BodyInternal != null) return;

        // GET request don't have a request body to read
        if (!HasBody)
            throw new BodyNotFoundException("Request don't have a body. " +
                                            "Please verify that this request is a Http POST/PUT/PATCH and request " +
                                            "content length is greater than zero before accessing the body.");

        if (!IsBodyRead)
        {
            if (Locked) throw new Exception("You cannot get the request body after request is made to server.");

            if (throwWhenNotReadYet)
                throw new Exception("Request body is not read yet. " +
                                    "Use SessionEventArgs.GetRequestBody() or SessionEventArgs.GetRequestBodyAsString() " +
                                    "method to read the request body.");
        }
    }

    internal static void ParseRequestLine(System.ReadOnlySpan<byte> httpCmd, out string method, out ByteString requestUri,
        out Version version)
    {
        var firstSpace = httpCmd.IndexOf((byte)' ');
        if (firstSpace == -1)
            throw new Exception("Invalid HTTP request line");

        var lastSpace = httpCmd.LastIndexOf((byte)' ');

        var methodSpan = httpCmd.Slice(0, firstSpace);
        method = System.Text.Encoding.ASCII.GetString(methodSpan.ToArray());
        if (!IsAllUpper(method)) method = method.ToUpper();

        version = HttpHeader.Version11;

        if (firstSpace == lastSpace)
        {
            var uriSpan = httpCmd.Slice(firstSpace + 1);
            requestUri = new ByteString(uriSpan.ToArray());
        }
        else
        {
            var uriSpan = httpCmd.Slice(firstSpace + 1, lastSpace - firstSpace - 1);
            requestUri = new ByteString(uriSpan.ToArray());

            var httpVersion = httpCmd.Slice(lastSpace + 1);

            var versionStr = System.Text.Encoding.ASCII.GetString(httpVersion.ToArray());
            if (versionStr.Equals("HTTP/1.0", System.StringComparison.OrdinalIgnoreCase)) version = HttpHeader.Version10;
        }
    }

    private static bool IsAllUpper(string input)
    {
        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            if (ch < 'A' || ch > 'Z') return false;
        }

        return true;
    }
}
