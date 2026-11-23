using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Titanium.Lev.Proxy.Http;
using Titanium.Lev.Proxy.StreamExtended.BufferPool;

namespace Titanium.Lev.Proxy.Helpers;

internal sealed class HttpServerStream : HttpStream
{
    internal HttpServerStream(ProxyServer server, Stream stream, IBufferPool bufferPool,
        CancellationToken cancellationToken)
        : base(server, stream, bufferPool, cancellationToken)
    {
    }

    /// <summary>
    ///     Writes the request.
    /// </summary>
    /// <param name="request">The request object.</param>
    /// <param name="cancellationToken">Optional cancellation token for this async task.</param>
    /// <returns></returns>
    internal async ValueTask WriteRequestAsync(Request request, CancellationToken cancellationToken = default)
    {
        var headerBuilder = new HeaderBuilder();
        headerBuilder.WriteRequestLine(request.Method, request.RequestUriString, request.HttpVersion);
        await WriteAsync(request, headerBuilder, cancellationToken);
    }

    internal async ValueTask<ResponseStatusInfo> ReadResponseStatus(CancellationToken cancellationToken = default)
    {
        var httpStatusMem = await ReadLineAsync(cancellationToken) ??
                         throw new IOException("Invalid http status code.");

        if (httpStatusMem.Length == 0)
            httpStatusMem = await ReadLineAsync(cancellationToken) ??
                         throw new IOException("Response status is empty.");

        Response.ParseResponseLine(httpStatusMem.Span, out var version, out var statusCode, out var description);
        return new ResponseStatusInfo { Version = version, StatusCode = statusCode, Description = description };
    }
}