using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Titanium.Lev.Proxy.Helpers;
using Titanium.Lev.Proxy.Http;
using Titanium.Lev.Proxy.Http.Responses;
using Titanium.Lev.Proxy.Models;
using Titanium.Lev.Proxy.StreamExtended.Network;

namespace Titanium.Lev.Proxy.EventArguments;

/// <summary>
/// Holds info related to a single proxy session (single request/response sequence).
/// A proxy session is bounded to a single connection from client.
/// A proxy session ends when client terminates connection to proxy
/// or when server terminates connection from proxy.
/// </summary>
public class SessionEventArgs : SessionEventArgsBase
{
    private bool disposed;

    /// <summary>
    /// Backing field for corresponding public property
    /// </summary>
    private bool reRequest;

    private WebSocketDecoder? webSocketDecoderReceive;

    private WebSocketDecoder? webSocketDecoderSend;

    /// <summary>
    /// Constructor to initialize the proxy
    /// </summary>
    internal SessionEventArgs(ProxyServer server, ProxyEndPoint endPoint, HttpClientStream clientStream, ConnectRequest? connectRequest, CancellationTokenSource cancellationTokenSource)
        : base(server, endPoint, clientStream, connectRequest, new Request(), cancellationTokenSource)
    {
    }

    /// <summary>
    ///     Is this session a HTTP/2 promise?
    /// </summary>
    public bool IsPromise { get; internal set; }

    private bool HasMulipartEventSubscribers => MultipartRequestPartSent != null;

    /// <summary>
    /// Should we send the request again ?
    /// </summary>
    public bool ReRequest
    {
        get => reRequest;
        set
        {
            if (HttpClient.Response.StatusCode == 0) throw new Exception("Response status code is empty. Cannot request again a request " + "which was never send to server.");

            reRequest = value;
        }
    }

    [Obsolete("Use [WebSocketDecoderReceive] instead")]
    public WebSocketDecoder WebSocketDecoder => WebSocketDecoderReceive;

    public WebSocketDecoder WebSocketDecoderSend => webSocketDecoderSend ??= new WebSocketDecoder(BufferPool);

    public WebSocketDecoder WebSocketDecoderReceive => webSocketDecoderReceive ??= new WebSocketDecoder(BufferPool);

    /// <summary>
    /// Occurs when multipart request part sent.
    /// </summary>
    public event EventHandler<MultipartRequestPartSentEventArgs>? MultipartRequestPartSent;

    /// <summary>
    /// Read request body content as bytes[] for current session
    /// </summary>
    private async Task ReadRequestBodyAsync(bool decompress, CancellationToken cancellationToken)
    {
        HttpClient.Request.EnsureBodyAvailable(false);

        var request = HttpClient.Request;

        // If not already read (not cached yet)
        if (!request.IsBodyRead)
        {
            if (request.IsBodyReceived) throw new Exception("Request body was already received.");

            if (request.HttpVersion == HttpHeader.Version20)
            {
                // do not send to the remote endpoint
                request.Http2IgnoreBodyFrames = true;

                request.Http2BodyData = MemoryStreamManager.Instance.GetStream();

                var tcs = new TaskCompletionSource<bool>();
                request.ReadHttp2BodyTaskCompletionSource = tcs;

                // signal to HTTP/2 copy frame method to continue
                request.ReadHttp2BeforeHandlerTaskCompletionSource!.SetResult(true);

                await tcs.Task.ConfigureAwait(false);

                // Now set the flag to true
                // So that next time we can deliver body from cache
                request.IsBodyRead = true;
                request.IsBodyReceived = true;
                request.IsBodyCompressed = !decompress && Titanium.Lev.Proxy.Compression.CompressionUtil.CompressionNameToEnum(request.ContentEncoding) != Titanium.Lev.Proxy.Compression.HttpCompression.Unsupported;
            }
            else
            {
                var body = await ReadBodyAsync(true, decompress, cancellationToken).ConfigureAwait(false);
                if (!request.BodyAvailable) request.BodyStream = body;
                else body.Dispose();

                // Now set the flag to true
                // So that next time we can deliver body from cache
                request.IsBodyRead = true;
                request.IsBodyReceived = true;
                request.IsBodyCompressed = !decompress && Titanium.Lev.Proxy.Compression.CompressionUtil.CompressionNameToEnum(request.ContentEncoding) != Titanium.Lev.Proxy.Compression.HttpCompression.Unsupported;
            }
        }
    }

    /// <summary>
    /// reinit response object
    /// </summary>
    internal async Task ClearResponse(CancellationToken cancellationToken)
    {
        // syphon out the response body from server
        await SyphonOutBodyAsync(false, cancellationToken).ConfigureAwait(false);
        HttpClient.Response = new Response();
    }

    internal void OnMultipartRequestPartSent(ReadOnlySpan<char> boundary, HeaderCollection headers)
    {
        try
        {
            MultipartRequestPartSent?.Invoke(this, new MultipartRequestPartSentEventArgs(this, boundary.ToString(), headers));
        }
        catch (Exception ex)
        {
            OnException(new Exception("Exception thrown in user event", ex));
        }
    }

    /// <summary>
    /// Read response body as byte[] for current response
    /// </summary>
    private async Task ReadResponseBodyAsync(bool decompress, CancellationToken cancellationToken)
    {
        if (!HttpClient.Request.Locked) throw new Exception("You cannot read the response body before request is made to server.");

        var response = HttpClient.Response;
        if (!response.HasBody) return;

        // If not already read (not cached yet)
        if (!response.IsBodyRead)
        {
            if (response.IsBodyReceived) throw new Exception("Response body was already received.");

            if (response.HttpVersion == HttpHeader.Version20)
            {
                // do not send to the remote endpoint
                response.Http2IgnoreBodyFrames = true;

                response.Http2BodyData = MemoryStreamManager.Instance.GetStream();

                var tcs = new TaskCompletionSource<bool>();
                response.ReadHttp2BodyTaskCompletionSource = tcs;

                // signal to HTTP/2 copy frame method to continue
                response.ReadHttp2BeforeHandlerTaskCompletionSource!.SetResult(true);

                await tcs.Task.ConfigureAwait(false);

                // Now set the flag to true
                // So that next time we can deliver body from cache
                response.IsBodyRead = true;
                response.IsBodyReceived = true;
                response.IsBodyCompressed = !decompress && Titanium.Lev.Proxy.Compression.CompressionUtil.CompressionNameToEnum(response.ContentEncoding) != Titanium.Lev.Proxy.Compression.HttpCompression.Unsupported;
            }
            else
            {
                var body = await ReadBodyAsync(false, decompress, cancellationToken).ConfigureAwait(false);
                if (!response.BodyAvailable) response.BodyStream = body;
                else body.Dispose();

                // Now set the flag to true
                // So that next time we can deliver body from cache
                response.IsBodyRead = true;
                response.IsBodyReceived = true;
                response.IsBodyCompressed = !decompress && Titanium.Lev.Proxy.Compression.CompressionUtil.CompressionNameToEnum(response.ContentEncoding) != Titanium.Lev.Proxy.Compression.HttpCompression.Unsupported;
            }
        }
    }

    private async Task<Microsoft.IO.RecyclableMemoryStream> ReadBodyAsync(bool isRequest, bool decompress, CancellationToken cancellationToken)
    {
        var bodyStream = MemoryStreamManager.Instance.GetStream();
        using var writer = new HttpStream(Server, bodyStream, BufferPool, cancellationToken, leaveOpen: true);

        if (isRequest)
            await CopyRequestBodyAsync(writer, decompress ? TransformationMode.Uncompress : TransformationMode.RemoveChunked, cancellationToken).ConfigureAwait(false);
        else
            await CopyResponseBodyAsync(writer, decompress ? TransformationMode.Uncompress : TransformationMode.RemoveChunked, cancellationToken).ConfigureAwait(false);

        return bodyStream;
    }

    /// <summary>
    ///     Syphon out any left over data in given request/response from backing tcp connection.
    ///     When user modifies the response/request we need to do this to reuse tcp connections.
    /// </summary>
    /// <param name="isRequest"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    internal async Task SyphonOutBodyAsync(bool isRequest, CancellationToken cancellationToken)
    {
        var requestResponse = isRequest ? (RequestResponseBase)HttpClient.Request : HttpClient.Response;
        if (requestResponse.IsBodyReceived || !requestResponse.OriginalHasBody) return;

        var reader = isRequest ? (HttpStream)ClientStream : HttpClient.Connection.Stream;

        await reader.CopyBodyAsync(requestResponse, true, NullWriter.Instance, TransformationMode.None, isRequest, this, cancellationToken).ConfigureAwait(false);
        requestResponse.IsBodyReceived = true;
    }

    /// <summary>
    ///  This is called when the request is PUT/POST/PATCH to read the body
    /// </summary>
    /// <returns></returns>
    internal async Task CopyRequestBodyAsync(IHttpStreamWriter writer, TransformationMode transformation, CancellationToken cancellationToken)
    {
        var request = HttpClient.Request;
        var reader = ClientStream;

        var contentLength = request.ContentLength;

        // send the request body bytes to server
        if (contentLength > 0 && HasMulipartEventSubscribers && request.IsMultipartFormData)
        {
            var boundary = HttpHelper.GetBoundaryFromContentType(request.ContentType);

            using (var copyStream = new CopyStream(reader, writer, BufferPool))
            {
                while (contentLength > copyStream.ReadBytes)
                {
                    var read = await ReadUntilBoundaryAsync(copyStream, contentLength, boundary, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;

                    if (contentLength > copyStream.ReadBytes)
                    {
                        var headers = new HeaderCollection();
                        await HeaderParser.ReadHeaders(copyStream, headers, cancellationToken).ConfigureAwait(false);
                        OnMultipartRequestPartSent(boundary.Span, headers);
                    }
                }

                await copyStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await reader.CopyBodyAsync(request, false, writer, transformation, true, this, cancellationToken).ConfigureAwait(false);
        }

        request.IsBodyReceived = true;
    }

    private async Task CopyResponseBodyAsync(IHttpStreamWriter writer, TransformationMode transformation, CancellationToken cancellationToken)
    {
        var response = HttpClient.Response;
        await HttpClient.Connection.Stream.CopyBodyAsync(response, false, writer, transformation, false, this, cancellationToken).ConfigureAwait(false);
        response.IsBodyReceived = true;
    }

    /// <summary>
    /// Read a line from the byte stream
    /// </summary>
    /// <returns></returns>
    private async Task<long> ReadUntilBoundaryAsync(ILineStream reader, long totalBytesToRead, ReadOnlyMemory<char> boundary, CancellationToken cancellationToken)
    {
        var bufferDataLength = 0;

        var buffer = BufferPool.GetBuffer();
        try
        {
            var boundaryLength = boundary.Length + 4;
            long bytesRead = 0;

            while (bytesRead < totalBytesToRead && (reader.DataAvailable || await reader.FillBufferAsync(cancellationToken)))
            {
                var newChar = reader.ReadByteFromBuffer();
                buffer[bufferDataLength] = newChar;

                bufferDataLength++;
                bytesRead++;

                if (bufferDataLength >= boundaryLength)
                {
                    // Fast path check: the boundary sequence ends with \r\n
                    if (buffer[bufferDataLength - 1] == '\n' && buffer[bufferDataLength - 2] == '\r')
                    {
                        var startIdx = bufferDataLength - boundaryLength;
                        if (buffer[startIdx] == '-' && buffer[startIdx + 1] == '-')
                        {
                            startIdx += 2;
                            var ok = true;
                            for (var i = 0; i < boundary.Length; i++)
                                if (buffer[startIdx + i] != boundary.Span[i])
                                {
                                    ok = false;
                                    break;
                                }

                            if (ok) break;
                        }
                    }
                }

                if (bufferDataLength == buffer.Length)
                {
                    // boundary is not longer than 70 bytes according to the specification, so keeping the last 100 (minimum 74) bytes is enough
                    const int bytesToKeep = 100;
                    Buffer.BlockCopy(buffer, buffer.Length - bytesToKeep, buffer, 0, bytesToKeep);
                    bufferDataLength = bytesToKeep;
                }
            }

            return bytesRead;
        }
        finally
        {
            BufferPool.ReturnBuffer(buffer);
        }
    }

    /// <summary>
    /// Gets the request body as a RecyclableMemoryStream.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token for this async task.</param>
    /// <returns>The body as stream.</returns>
    public Task<Microsoft.IO.RecyclableMemoryStream> GetRequestBodyStream(CancellationToken cancellationToken = default) => GetRequestBodyStream(true, cancellationToken);
    public async Task<Microsoft.IO.RecyclableMemoryStream> GetRequestBodyStream(bool decompress, CancellationToken cancellationToken = default)
    {
        if (!HttpClient.Request.IsBodyRead) await ReadRequestBodyAsync(decompress, cancellationToken).ConfigureAwait(false);
        return HttpClient.Request.BodyStream;
    }

    /// <summary>
    /// Gets the request body as string.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token for this async task.</param>
    /// <returns>The body as string.</returns>
    public Task<string> GetRequestBodyAsString(CancellationToken cancellationToken = default) => GetRequestBodyAsString(true, cancellationToken);
    public async Task<string> GetRequestBodyAsString(bool decompress, CancellationToken cancellationToken = default)
    {
        if (!HttpClient.Request.IsBodyRead) await ReadRequestBodyAsync(decompress, cancellationToken).ConfigureAwait(false);
        return HttpClient.Request.BodyString;
    }

    /// <summary>
    /// Sets the request body.
    /// </summary>
    /// <param name="body">The request body stream.</param>
    public void SetRequestBodyStream(Microsoft.IO.RecyclableMemoryStream body)
    {
        var request = HttpClient.Request;
        if (request.Locked) throw new Exception("You cannot call this function after request is made to server.");

        request.IsChunked = false;
        request.BodyStream = body;
        request.IsBodyRead = true;
        request.IsBodyReceived = true;
        request.IsBodyCompressed = false;
    }

    /// <summary>
    /// Sets the body with the specified byte array.
    /// </summary>
    /// <param name="body">The request body byte array to set.</param>
    public void SetRequestBody(byte[] body)
    {
        if (HttpClient.Request.Locked) throw new Exception("You cannot call this function after request is made to server.");

        var stream = MemoryStreamManager.Instance.GetStream();
        stream.Write(body, 0, body.Length);
        stream.Position = 0;
        SetRequestBodyStream(stream);
    }


    /// <summary>
    /// Gets the response body as stream.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token for this async task.</param>
    /// <returns>The resulting stream.</returns>
    public Task<Microsoft.IO.RecyclableMemoryStream> GetResponseBodyStream(CancellationToken cancellationToken = default) => GetResponseBodyStream(true, cancellationToken);
    public async Task<Microsoft.IO.RecyclableMemoryStream> GetResponseBodyStream(bool decompress, CancellationToken cancellationToken = default)
    {
        if (!HttpClient.Response.IsBodyRead) await ReadResponseBodyAsync(decompress, cancellationToken).ConfigureAwait(false);
        return HttpClient.Response.BodyStream;
    }

    /// <summary>
    /// Gets the response body as string.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token for this async task.</param>
    /// <returns>The string body.</returns>
    public Task<string> GetResponseBodyAsString(CancellationToken cancellationToken = default) => GetResponseBodyAsString(true, cancellationToken);
    public async Task<string> GetResponseBodyAsString(bool decompress, CancellationToken cancellationToken = default)
    {
        if (!HttpClient.Response.IsBodyRead) await ReadResponseBodyAsync(decompress, cancellationToken).ConfigureAwait(false);
        return HttpClient.Response.BodyString;
    }

    /// <summary>
    /// Set the response body stream.
    /// </summary>
    /// <param name="body">The body stream to set.</param>
    public void SetResponseBodyStream(Microsoft.IO.RecyclableMemoryStream body)
    {
        if (!HttpClient.Request.Locked) throw new Exception("You cannot call this function before request is made to server.");

        var response = HttpClient.Response;

        // If the original response had a body but the user never read it, 
        // the server connection still has unread data. Reusing this connection 
        // will desync the HTTP pipeline or cause garbage reads, so we must close it.
        if (response.OriginalHasBody && !response.IsBodyRead)
        {
            HttpClient.CloseServerConnection = true;
        }

        // response.IsChunked = false; // KEEP CHUNKED IF ORIGINAL WAS CHUNKED
        response.BodyStream = body;
        response.IsBodyRead = true;
        response.IsBodyReceived = true;
        response.IsBodyCompressed = false;
    }

    /// <summary>
    /// Replace the response body with the specified byte array.
    /// </summary>
    /// <param name="body">The body byte array to set.</param>
    public void SetResponseBody(byte[] body)
    {
        if (!HttpClient.Request.Locked) throw new Exception("You cannot call this function before request is made to server.");

        var stream = MemoryStreamManager.Instance.GetStream();
        stream.Write(body, 0, body.Length);
        stream.Position = 0;
        SetResponseBodyStream(stream);
    }

    /// <summary>
    /// Before request is made to server respond with the specified HTML string to client
    /// and ignore the request. 
    /// </summary>
    /// <param name="html">HTML content to sent.</param>
    /// <param name="headers">HTTP response headers.</param>
    /// <param name="closeServerConnection">Close the server connection used by request if any?</param>
    public void Ok(string html, IDictionary<string, HttpHeader>? headers,
        bool closeServerConnection = false)
    {
        Ok(html, headers?.Values, closeServerConnection);
    }

    /// <summary>
    /// Before request is made to server respond with the specified HTML string to client
    /// and ignore the request. 
    /// </summary>
    /// <param name="html">HTML content to sent.</param>
    /// <param name="headers">HTTP response headers.</param>
    /// <param name="closeServerConnection">Close the server connection used by request if any?</param>
    public void Ok(string html, IEnumerable<HttpHeader>? headers = null,
        bool closeServerConnection = false)
    {
        var response = new OkResponse();
        if (headers != null) response.Headers.AddHeaders(headers);

        response.HttpVersion = HttpClient.Request.HttpVersion;
        var stream = MemoryStreamManager.Instance.GetStream();
        var bytes = response.Encoding.GetBytes(html ?? string.Empty);
        stream.Write(bytes, 0, bytes.Length);
        stream.Position = 0;
        response.BodyStream = stream;

        Respond(response, closeServerConnection);
    }

    /// <summary>
    /// Before request is made to server respond with the specified byte[] to client
    /// and ignore the request. 
    /// </summary>
    /// <param name="result">The html content bytes.</param>
    /// <param name="headers">The HTTP headers.</param>
    /// <param name="closeServerConnection">Close the server connection used by request if any?</param>
    public void Ok(byte[] result, IDictionary<string, HttpHeader>? headers,
        bool closeServerConnection = false)
    {
        Ok(result, headers?.Values, closeServerConnection);
    }

    /// <summary>
    /// Before request is made to server respond with the specified byte[] to client
    /// and ignore the request. 
    /// </summary>
    /// <param name="result">The html content bytes.</param>
    /// <param name="headers">The HTTP headers.</param>
    /// <param name="closeServerConnection">Close the server connection used by request if any?</param>
    public void Ok(byte[] result, IEnumerable<HttpHeader>? headers = null,
        bool closeServerConnection = false)
    {
        var response = new OkResponse();
        response.Headers.AddHeaders(headers);
        response.HttpVersion = HttpClient.Request.HttpVersion;
        var stream = MemoryStreamManager.Instance.GetStream();
        stream.Write(result, 0, result.Length);
        stream.Position = 0;
        response.BodyStream = stream;

        Respond(response, closeServerConnection);
    }

    /// <summary>
    /// Before request is made to server 
    /// respond with the specified HTML string and the specified status to client.
    /// And then ignore the request. 
    /// </summary>
    /// <param name="html">The html content.</param>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="headers">The HTTP headers.</param>
    /// <param name="closeServerConnection">Close the server connection used by request if any?</param>
    public void GenericResponse(string html, HttpStatusCode status,
        IDictionary<string, HttpHeader>? headers, bool closeServerConnection = false)
    {
        GenericResponse(html, status, headers?.Values, closeServerConnection);
    }

    /// <summary>
    /// Before request is made to server 
    /// respond with the specified HTML string and the specified status to client.
    /// And then ignore the request. 
    /// </summary>
    /// <param name="html">The html content.</param>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="headers">The HTTP headers.</param>
    /// <param name="closeServerConnection">Close the server connection used by request if any?</param>
    public void GenericResponse(string html, HttpStatusCode status,
        IEnumerable<HttpHeader>? headers = null, bool closeServerConnection = false)
    {
        var response = new GenericResponse(status);
        response.HttpVersion = HttpClient.Request.HttpVersion;
        response.Headers.AddHeaders(headers);
        var stream = MemoryStreamManager.Instance.GetStream();
        var bytes = response.Encoding.GetBytes(html ?? string.Empty);
        stream.Write(bytes, 0, bytes.Length);
        stream.Position = 0;
        response.BodyStream = stream;

        Respond(response, closeServerConnection);
    }

    /// <summary>
    /// Before request is made to server respond with the specified byte[],
    /// the specified status  to client. And then ignore the request.
    /// </summary>
    /// <param name="result">The bytes to sent.</param>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="headers">The HTTP headers.</param>
    /// <param name="closeServerConnection">Close the server connection used by request if any?</param>
    public void GenericResponse(byte[] result, HttpStatusCode status,
        IDictionary<string, HttpHeader> headers, bool closeServerConnection = false)
    {
        GenericResponse(result, status, headers?.Values, closeServerConnection);
    }

    /// <summary>
    /// Before request is made to server respond with the specified byte[],
    /// the specified status  to client. And then ignore the request.
    /// </summary>
    /// <param name="result">The bytes to sent.</param>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="headers">The HTTP headers.</param>
    /// <param name="closeServerConnection">Close the server connection used by request if any?</param>
    public void GenericResponse(byte[] result, HttpStatusCode status,
        IEnumerable<HttpHeader>? headers, bool closeServerConnection = false)
    {
        var response = new GenericResponse(status);
        response.HttpVersion = HttpClient.Request.HttpVersion;
        response.Headers.AddHeaders(headers);
        var stream = MemoryStreamManager.Instance.GetStream();
        stream.Write(result, 0, result.Length);
        stream.Position = 0;
        response.BodyStream = stream;

        Respond(response, closeServerConnection);
    }

    /// <summary>
    /// Redirect to provided URL.
    /// </summary>
    /// <param name="url">The URL to redirect.</param>
    /// <param name="closeServerConnection">Close the server connection used by request if any?</param>
    public void Redirect(string url, bool closeServerConnection = false)
    {
        var response = new RedirectResponse();
        response.HttpVersion = HttpClient.Request.HttpVersion;
        response.Headers.AddHeader(KnownHeaders.Location, url);
        response.BodyStream = MemoryStreamManager.Instance.GetStream();

        Respond(response, closeServerConnection);
    }

    /// <summary>
    /// Respond with given response object to client.
    /// </summary>
    /// <param name="response">The response object.</param>
    /// <param name="closeServerConnection">Close the server connection used by request if any?</param>
    public void Respond(Response response, bool closeServerConnection = false)
    {
        // request already send/ready to be sent.
        if (HttpClient.Request.Locked)
        {
            // response already received from server and ready to be sent to client.
            if (HttpClient.Response.Locked) throw new Exception("You cannot call this function after response is sent to the client.");

            // cleanup original response.
            if (closeServerConnection)
            // no need to cleanup original connection.
            // it will be closed any way.
                TerminateServerConnection();

            response.SetOriginalHeaders(HttpClient.Response);

            // response already received from server but not yet ready to sent to client.         
            HttpClient.Response = response;
            HttpClient.Response.Locked = true;
        }
        // request not yet sent/not yet ready to be sent.
        else
        {
            HttpClient.Request.Locked = true;
            HttpClient.Request.CancelRequest = true;

            // set new response.
            HttpClient.Response = response;
            HttpClient.Response.Locked = true;
        }
    }

    /// <summary>
    ///     Terminate the connection to server at the end of this HTTP request/response session.
    /// </summary>
    public void TerminateServerConnection()
    {
        HttpClient.CloseServerConnection = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposed) return;

        MultipartRequestPartSent = null;
        disposed = true;

        base.Dispose(disposing);
    }

    ~SessionEventArgs()
    {
#if DEBUG
            // Finalizer should not be called
            System.Diagnostics.Debugger.Break();
#endif

        Dispose(false);
    }
}





