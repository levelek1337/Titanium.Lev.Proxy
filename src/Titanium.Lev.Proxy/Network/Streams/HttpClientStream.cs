using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Titanium.Lev.Proxy.Http;
using Titanium.Lev.Proxy.Network.Tcp;
using Titanium.Lev.Proxy.StreamExtended.BufferPool;

namespace Titanium.Lev.Proxy.Helpers;

internal sealed class HttpClientStream : HttpStream
{
    internal HttpClientStream(ProxyServer server, TcpClientConnection connection, Stream stream, IBufferPool bufferPool,
        CancellationToken cancellationToken)
        : base(server, stream, bufferPool, cancellationToken)
    {
        Connection = connection;
    }

    public TcpClientConnection Connection { get; }

    /// <summary>
    ///     Writes the response.
    /// </summary>
    /// <param name="response">The response object.</param>
    /// <param name="cancellationToken">Optional cancellation token for this async task.</param>
    /// <returns>The Task.</returns>
    internal async ValueTask WriteResponseAsync(Response response, CancellationToken cancellationToken = default)
    {
        var headerBuilder = new HeaderBuilder();

        // Write back response status to client
        headerBuilder.WriteResponseLine(response.HttpVersion, response.StatusCode, response.StatusDescription);

        await WriteAsync(response, headerBuilder, cancellationToken);
    }

    internal async ValueTask<RequestStatusInfo> ReadRequestLine(CancellationToken cancellationToken = default)
    {
        var httpCmdMem = await ReadLineAsync(cancellationToken);
        if (httpCmdMem == null || httpCmdMem.Value.Length == 0) return default;

        Request.ParseRequestLine(httpCmdMem.Value.Span, out var method, out var requestUri, out var version);

        return new RequestStatusInfo { Method = method, RequestUri = requestUri, Version = version };
    }
}