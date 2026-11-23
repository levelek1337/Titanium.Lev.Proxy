using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Titanium.Lev.Proxy.EventArguments;
using Titanium.Lev.Proxy.Extensions;
using Titanium.Lev.Proxy.Helpers;
using Titanium.Lev.Proxy.Http;
using Titanium.Lev.Proxy.Models;
using Titanium.Lev.Proxy.ProxySocket;

namespace Titanium.Lev.Proxy.Network.Tcp;

/// <summary>
///     A class that manages Tcp Connection to server used by this proxy server.
/// </summary>
internal class TcpConnectionFactory : IDisposable
{
    // Tcp server connection pool cache
    private readonly ConcurrentDictionary<string, ConcurrentQueue<TcpServerConnection>> cache = new();

    // Tcp connections waiting to be disposed by cleanup task
    
    private bool disposed;

    private volatile bool runCleanUpTask = true;

    internal TcpConnectionFactory(ProxyServer server)
    {
        Server = server;
        Task.Run(async () => await ClearOutdatedConnections()).ConfigureAwait(false);
    }

    internal ProxyServer Server { get; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    internal string GetConnectionCacheKey(string remoteHostName, int remotePort,
        bool isHttps, List<SslApplicationProtocol>? applicationProtocols,
        IPEndPoint? upStreamEndPoint, IExternalProxy? externalProxy)
    {
        // http version is ignored since its an application level decision b/w HTTP 1.0/1.1
        // also when doing connect request MS Edge browser sends http 1.0 but uses 1.1 after server sends 1.1 its response.
        // That can create cache miss for same server connection unnecessarily especially when prefetching with Connect.
        // http version 2 is separated using applicationProtocols below.
        var cacheKeyBuilder = new StringBuilder();
        cacheKeyBuilder.Append(remoteHostName);
        cacheKeyBuilder.Append("-");
        cacheKeyBuilder.Append(remotePort);
        cacheKeyBuilder.Append("-");

        // when creating Tcp client isConnect won't matter
        cacheKeyBuilder.Append(isHttps);

        if (applicationProtocols != null)
            foreach (var protocol in applicationProtocols.OrderBy(x => x))
            {
                cacheKeyBuilder.Append("-");
                cacheKeyBuilder.Append(protocol);
            }

        if (upStreamEndPoint != null)
        {
            cacheKeyBuilder.Append("-");
            cacheKeyBuilder.Append(upStreamEndPoint.Address);
            cacheKeyBuilder.Append("-");
            cacheKeyBuilder.Append(upStreamEndPoint.Port);
        }

        if (externalProxy != null)
        {
            cacheKeyBuilder.Append("-");
            cacheKeyBuilder.Append(externalProxy.HostName);
            cacheKeyBuilder.Append("-");
            cacheKeyBuilder.Append(externalProxy.Port);
            cacheKeyBuilder.Append("-");
            cacheKeyBuilder.Append(externalProxy.ProxyType);

            if (externalProxy.UseDefaultCredentials)
            {
                cacheKeyBuilder.Append("-");
                cacheKeyBuilder.Append(externalProxy.UserName);
                cacheKeyBuilder.Append("-");
                cacheKeyBuilder.Append(externalProxy.Password);
            }
        }

        return cacheKeyBuilder.ToString();
    }

    /// <summary>
    ///     Gets the connection cache key.
    /// </summary>
    /// <param name="server">The server.</param>
    /// <param name="session">The session event arguments.</param>
    /// <param name="applicationProtocol">The application protocol.</param>
    /// <returns></returns>
    internal async Task<string> GetConnectionCacheKey(ProxyServer server, SessionEventArgsBase session,
        SslApplicationProtocol applicationProtocol)
    {
        List<SslApplicationProtocol>? applicationProtocols = null;
        if (applicationProtocol != default)
            applicationProtocols = new List<SslApplicationProtocol> { applicationProtocol };

        var customUpStreamProxy = session.CustomUpStreamProxy;

        var isHttps = session.IsHttps;
        if (customUpStreamProxy == null && server.GetCustomUpStreamProxyFunc != null)
            customUpStreamProxy = await server.GetCustomUpStreamProxyFunc(session).ConfigureAwait(false);

        session.CustomUpStreamProxyUsed = customUpStreamProxy;

        var uri = session.HttpClient.Request.RequestUri;
        var upStreamEndPoint = session.HttpClient.UpStreamEndPoint ?? server.UpStreamEndPoint;
        var upStreamProxy = customUpStreamProxy ?? (isHttps ? server.UpStreamHttpsProxy : server.UpStreamHttpProxy);
        return GetConnectionCacheKey(uri.Host, uri.Port, isHttps, applicationProtocols, upStreamEndPoint,
            upStreamProxy);
    }


    /// <summary>
    ///     Create a server connection.
    /// </summary>
    /// <param name="proxyServer">The proxy server.</param>
    /// <param name="session">The session event arguments.</param>
    /// <param name="isConnect">Is this a CONNECT request.</param>
    /// <param name="applicationProtocol"></param>
    /// <param name="noCache">if set to <c>true</c> [no cache].</param>
    /// <param name="cancellationToken">The cancellation token for this async task.</param>
    /// <returns></returns>
    internal Task<TcpServerConnection> GetServerConnection(ProxyServer proxyServer, SessionEventArgsBase session,
        bool isConnect,
        SslApplicationProtocol applicationProtocol, bool noCache, CancellationToken cancellationToken)
    {
        List<SslApplicationProtocol>? applicationProtocols = null;
        if (applicationProtocol != default)
            applicationProtocols = new List<SslApplicationProtocol> { applicationProtocol };

        return GetServerConnection(proxyServer, session, isConnect, applicationProtocols, noCache, false,
            cancellationToken)!;
    }

    /// <summary>
    ///     Create a server connection.
    /// </summary>
    /// <param name="proxyServer">The proxy server.</param>
    /// <param name="session">The session event arguments.</param>
    /// <param name="isConnect">Is this a CONNECT request.</param>
    /// <param name="applicationProtocols"></param>
    /// <param name="noCache">if set to <c>true</c> [no cache].</param>
    /// <param name="prefetch">if set to <c>true</c> [prefetch].</param>
    /// <param name="cancellationToken">The cancellation token for this async task.</param>
    /// <returns></returns>
    internal async Task<TcpServerConnection?> GetServerConnection(ProxyServer proxyServer, SessionEventArgsBase session,
        bool isConnect,
        List<SslApplicationProtocol>? applicationProtocols, bool noCache, bool prefetch,
        CancellationToken cancellationToken)
    {
        var customUpStreamProxy = session.CustomUpStreamProxy;

        var isHttps = session.IsHttps;
        if (customUpStreamProxy == null && proxyServer.GetCustomUpStreamProxyFunc != null)
            customUpStreamProxy = await proxyServer.GetCustomUpStreamProxyFunc(session).ConfigureAwait(false);

        session.CustomUpStreamProxyUsed = customUpStreamProxy;

        var request = session.HttpClient.Request;
        string host;
        int port;
        if (request.Authority.Length > 0)
        {
            var authority = request.Authority;
            var idx = authority.IndexOf((byte)':');
            if (idx == -1)
            {
                host = authority.GetString();
                port = 80;
            }
            else
            {
                host = authority.Slice(0, idx).GetString();
                port = int.Parse(authority.Slice(idx + 1).GetString());
            }
        }
        else
        {
            var uri = request.RequestUri;
            host = uri.Host;
            port = uri.Port;
        }

        var upStreamEndPoint = session.HttpClient.UpStreamEndPoint ?? proxyServer.UpStreamEndPoint;
        var upStreamProxy = customUpStreamProxy ??
                            (isHttps ? proxyServer.UpStreamHttpsProxy : proxyServer.UpStreamHttpProxy);
        return await GetServerConnection(proxyServer, host, port, session.HttpClient.Request.HttpVersion, isHttps,
            applicationProtocols, isConnect, session, upStreamEndPoint, upStreamProxy, noCache, prefetch,
            cancellationToken);
    }

    /// <summary>
    ///     Gets a TCP connection to server from connection pool.
    /// </summary>
    /// <param name="proxyServer">The current ProxyServer instance.</param>
    /// <param name="remoteHostName">The remote hostname.</param>
    /// <param name="remotePort">The remote port.</param>
    /// <param name="httpVersion">The http version to use.</param>
    /// <param name="isHttps">Is this a HTTPS request.</param>
    /// <param name="applicationProtocols">The list of HTTPS application level protocol to negotiate if needed.</param>
    /// <param name="isConnect">Is this a CONNECT request.</param>
    /// <param name="sessionArgs">The session event arguments.</param>
    /// <param name="upStreamEndPoint">The local upstream endpoint to make request via.</param>
    /// <param name="externalProxy">The external proxy to make request via.</param>
    /// <param name="noCache">Not from cache/create new connection.</param>
    /// <param name="prefetch">if set to <c>true</c> [prefetch].</param>
    /// <param name="cancellationToken">The cancellation token for this async task.</param>
    /// <returns></returns>
    internal async Task<TcpServerConnection?> GetServerConnection(ProxyServer proxyServer, string remoteHostName,
        int remotePort,
        Version httpVersion, bool isHttps, List<SslApplicationProtocol>? applicationProtocols, bool isConnect,
        SessionEventArgsBase sessionArgs, IPEndPoint? upStreamEndPoint, IExternalProxy? externalProxy,
        bool noCache, bool prefetch, CancellationToken cancellationToken)
    {
        var sslProtocol = sessionArgs.ClientConnection.SslProtocol;
        var cacheKey = GetConnectionCacheKey(remoteHostName, remotePort,
            isHttps, applicationProtocols, upStreamEndPoint, externalProxy);

        if (proxyServer.EnableConnectionPool && !noCache)
            if (cache.TryGetValue(cacheKey, out var existingConnections))
            {
                // +3 seconds for potential delay after getting connection
                    var cutOff = DateTime.UtcNow.AddSeconds(-proxyServer.ConnectionTimeOutSeconds + 3);
                    while (existingConnections.TryDequeue(out var recentConnection))
                    {
                        if (recentConnection.LastAccess > cutOff && recentConnection.TcpSocket.IsGoodConnection())
                            return recentConnection;

                        recentConnection.Dispose();
                    }
            }

        var connection = await CreateServerConnection(remoteHostName, remotePort, httpVersion, isHttps, sslProtocol,
            applicationProtocols, isConnect, proxyServer, sessionArgs, upStreamEndPoint, externalProxy, cacheKey,
            prefetch, cancellationToken);

        return connection;
    }

    /// <summary>
    ///     Creates a TCP connection to server
    /// </summary>
    /// <param name="remoteHostName">The remote hostname.</param>
    /// <param name="remotePort">The remote port.</param>
    /// <param name="httpVersion">The http version to use.</param>
    /// <param name="isHttps">Is this a HTTPS request.</param>
    /// <param name="sslProtocol">The SSL protocol.</param>
    /// <param name="applicationProtocols">The list of HTTPS application level protocol to negotiate if needed.</param>
    /// <param name="isConnect">Is this a CONNECT request.</param>
    /// <param name="proxyServer">The current ProxyServer instance.</param>
    /// <param name="sessionArgs">The http session.</param>
    /// <param name="upStreamEndPoint">The local upstream endpoint to make request via.</param>
    /// <param name="externalProxy">The external proxy to make request via.</param>
    /// <param name="cacheKey">The connection cache key</param>
    /// <param name="prefetch">if set to <c>true</c> [prefetch].</param>
    /// <param name="cancellationToken">The cancellation token for this async task.</param>
    /// <returns></returns>
    private async Task<TcpServerConnection?> CreateServerConnection(string remoteHostName, int remotePort,
        Version httpVersion, bool isHttps, SslProtocols sslProtocol, List<SslApplicationProtocol>? applicationProtocols,
        bool isConnect,
        ProxyServer proxyServer, SessionEventArgsBase sessionArgs, IPEndPoint? upStreamEndPoint,
        IExternalProxy? externalProxy, string cacheKey,
        bool prefetch, CancellationToken cancellationToken)
    {
        // deny connection to proxy end points to avoid infinite connection loop.
        if (Server.ProxyEndPoints.Any(x => x.Port == remotePort)
            && NetworkHelper.IsLocalIpAddress(remoteHostName))
            throw new Exception(
                $"A client is making HTTP request to one of the listening ports of this proxy {remoteHostName}:{remotePort}");

        if (externalProxy != null)
            if (Server.ProxyEndPoints.Any(x => x.Port == externalProxy.Port)
                && NetworkHelper.IsLocalIpAddress(externalProxy.HostName))
                throw new Exception(
                    $"A client is making HTTP request via external proxy to one of the listening ports of this proxy {remoteHostName}:{remotePort}");

        if (proxyServer.SupportedServerSslProtocols != SslProtocols.None) sslProtocol = proxyServer.SupportedServerSslProtocols;

        if (isHttps && sslProtocol == SslProtocols.None) sslProtocol = proxyServer.SupportedSslProtocols;

        var useUpstreamProxy1 = false;

        // check if external proxy is set for HTTP/HTTPS
        if (externalProxy != null && !(externalProxy.HostName == remoteHostName && externalProxy.Port == remotePort))
        {
            useUpstreamProxy1 = true;

            // check if we need to ByPass
            if (externalProxy.BypassLocalhost &&
                NetworkHelper.IsLocalIpAddress(remoteHostName, externalProxy.ProxyDnsRequests))
                useUpstreamProxy1 = false;
        }

        if (!useUpstreamProxy1) externalProxy = null;

        Socket? tcpServerSocket = null;
        HttpServerStream? stream = null;

        SslApplicationProtocol negotiatedApplicationProtocol = default;

        var retry = true;
        var enabledSslProtocols = sslProtocol;

        retry:
        try
        {
            var socks = externalProxy != null && externalProxy.ProxyType != ExternalProxyType.Http;
            var hostname = remoteHostName;
            var port = remotePort;

            if (externalProxy != null)
            {
                hostname = externalProxy.HostName;
                port = externalProxy.Port;
            }

            var ipAddresses = await Dns.GetHostAddressesAsync(hostname).ConfigureAwait(false);
            if (ipAddresses == null || ipAddresses.Length == 0)
            {
                if (prefetch) return null;

                throw new Exception($"Could not resolve the hostname {hostname}");
            }

            if (sessionArgs != null) sessionArgs.TimeLine["Dns Resolved"] = DateTime.UtcNow;

            Array.Sort(ipAddresses, (x, y) => x.AddressFamily.CompareTo(y.AddressFamily));

            Exception? lastException = null;
            for (var i = 0; i < ipAddresses.Length; i++)
                try
                {
                    var ipAddress = ipAddresses[i];
                    var addressFamily = upStreamEndPoint?.AddressFamily ?? ipAddress.AddressFamily;

                    if (socks)
                    {
                        var proxySocket =
                            new ProxySocket.ProxySocket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
                        proxySocket.ProxyType = externalProxy!.ProxyType == ExternalProxyType.Socks4
                            ? ProxyTypes.Socks4
                            : ProxyTypes.Socks5;

                        proxySocket.ProxyEndPoint = new IPEndPoint(ipAddress, port);
                        if (!string.IsNullOrEmpty(externalProxy.UserName) && externalProxy.Password != null)
                        {
                            proxySocket.ProxyUser = externalProxy.UserName;
                            proxySocket.ProxyPass = externalProxy.Password;
                        }

                        tcpServerSocket = proxySocket;
                    }
                    else
                    {
                        tcpServerSocket = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
                    }

                    if (upStreamEndPoint != null) tcpServerSocket.Bind(upStreamEndPoint);

                    tcpServerSocket.NoDelay = proxyServer.NoDelay;
                    tcpServerSocket.ReceiveTimeout = proxyServer.ConnectionTimeOutSeconds * 1000;
                    tcpServerSocket.SendTimeout = proxyServer.ConnectionTimeOutSeconds * 1000;
                    tcpServerSocket.LingerState = new LingerOption(true, proxyServer.TcpTimeWaitSeconds);

                    if (proxyServer.ReuseSocket && RunTime.IsSocketReuseAvailable())
                        tcpServerSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                    Task connectTask;

                    if (socks)
                    {
                        if (externalProxy!.ProxyDnsRequests)
                        {
                            connectTask =
                                ProxySocketConnectionTaskFactory.CreateTask((ProxySocket.ProxySocket)tcpServerSocket,
                                    remoteHostName, remotePort);
                        }
                        else
                        {
                            // todo: resolve only once when the SOCKS proxy has multiple addresses (and the first address fails)
                            var remoteIpAddresses = await Dns.GetHostAddressesAsync(remoteHostName).ConfigureAwait(false);
                            if (remoteIpAddresses == null || remoteIpAddresses.Length == 0)
                                throw new Exception($"Could not resolve the SOCKS remote hostname {remoteHostName}");

                            // todo: use the 2nd, 3rd... remote addresses when first fails
                            connectTask = ProxySocketConnectionTaskFactory.CreateTask(
                                (ProxySocket.ProxySocket)tcpServerSocket, remoteIpAddresses[0], remotePort);
                        }
                    }
                    else
                    {
                        connectTask = SocketConnectionTaskFactory.CreateTask(tcpServerSocket, ipAddress, port);
                    }

                    await Task.WhenAny(connectTask,
                        Task.Delay(proxyServer.ConnectTimeOutSeconds * 1000, cancellationToken));
                    if (!connectTask.IsCompleted || !tcpServerSocket.Connected)
                    {
                        // here we can just do some cleanup and let the loop continue since
                        // we will either get a connection or wind up with a null tcpClient
                        // which will throw
                        try
                        {
                            connectTask.Dispose();
                        }
                        catch
                        {
                            // ignore
                        }

                        try
                        {
                            tcpServerSocket?.Dispose();
                            tcpServerSocket = null;
                        }
                        catch
                        {
                            // ignore
                        }

                        continue;
                    }

                    break;
                }
                catch (Exception e)
                {
                    // dispose the current TcpClient and try the next address
                    lastException = e;
                    tcpServerSocket?.Dispose();
                    tcpServerSocket = null;
                }

            if (tcpServerSocket == null)
            {
                if (sessionArgs != null && proxyServer.CustomUpStreamProxyFailureFunc != null)
                {
                    var newUpstreamProxy = await proxyServer.CustomUpStreamProxyFailureFunc(sessionArgs).ConfigureAwait(false);
                    if (newUpstreamProxy != null)
                    {
                        sessionArgs.CustomUpStreamProxyUsed = newUpstreamProxy;
                        sessionArgs.TimeLine["Retrying Upstream Proxy Connection"] = DateTime.UtcNow;
                        return await CreateServerConnection(remoteHostName, remotePort, httpVersion, isHttps,
                            sslProtocol, applicationProtocols, isConnect, proxyServer, sessionArgs, upStreamEndPoint,
                            externalProxy, cacheKey, prefetch, cancellationToken);
                    }
                }

                if (prefetch) return null;

                throw new Exception($"Could not establish connection to {hostname}", lastException);
            }

            if (sessionArgs != null) sessionArgs.TimeLine["Connection Established"] = DateTime.UtcNow;

            await proxyServer.InvokeServerConnectionCreateEvent(tcpServerSocket).ConfigureAwait(false);

            stream = new HttpServerStream(proxyServer, new NetworkStream(tcpServerSocket, true), proxyServer.BufferPool,
                cancellationToken);

            if (externalProxy != null && externalProxy.ProxyType == ExternalProxyType.Http && (isConnect || isHttps))
            {
                var authority = $"{remoteHostName}:{remotePort}";
                var authorityBytes = authority.GetByteString();
                var connectRequest = new ConnectRequest(authorityBytes)
                {
                    IsHttps = isHttps,
                    RequestUriString8 = authorityBytes,
                    HttpVersion = httpVersion
                };

                connectRequest.Headers.AddHeader(KnownHeaders.Connection, KnownHeaders.ConnectionKeepAlive);
                connectRequest.Headers.AddHeader(KnownHeaders.Host, authority);

                if (!string.IsNullOrEmpty(externalProxy.UserName) && externalProxy.Password != null)
                {
                    connectRequest.Headers.AddHeader(HttpHeader.ProxyConnectionKeepAlive);
                    connectRequest.Headers.AddHeader(
                        HttpHeader.GetProxyAuthorizationHeader(externalProxy.UserName, externalProxy.Password));
                }

                await proxyServer.OnBeforeUpStreamConnectRequest(connectRequest).ConfigureAwait(false);

                await stream.WriteRequestAsync(connectRequest, cancellationToken).ConfigureAwait(false);

                var httpStatus = await stream.ReadResponseStatus(cancellationToken).ConfigureAwait(false);
                var headers = new HeaderCollection();
                await HeaderParser.ReadHeaders(stream, headers, cancellationToken).ConfigureAwait(false);

                if (httpStatus.StatusCode != 200 && !httpStatus.Description.EqualsIgnoreCase("OK")
                                                 && !httpStatus.Description.EqualsIgnoreCase("Connection Established"))
                    throw new Exception("Upstream proxy failed to create a secure tunnel");
            }

            if (isHttps)
            {
                var sslStream = new SslStream(stream, false,
                    (sender, certificate, chain, sslPolicyErrors) =>
                        proxyServer.ValidateServerCertificate(sender, sessionArgs, certificate, chain,
                            sslPolicyErrors),
                    (sender, targetHost, localCertificates, remoteCertificate, acceptableIssuers) =>
                        proxyServer.SelectClientCertificate(sender, sessionArgs, targetHost, localCertificates,
                            remoteCertificate, acceptableIssuers));
                stream = new HttpServerStream(proxyServer, sslStream, proxyServer.BufferPool, cancellationToken);

                var options = new SslClientAuthenticationOptions
                {
                    ApplicationProtocols = applicationProtocols,
                    TargetHost = remoteHostName,
                    ClientCertificates = null!,
                    EnabledSslProtocols = enabledSslProtocols,
                    CertificateRevocationCheckMode = proxyServer.CheckCertificateRevocation
                };
                await sslStream.AuthenticateAsClientAsync(options, cancellationToken).ConfigureAwait(false);
#if NET6_0_OR_GREATER
                negotiatedApplicationProtocol = sslStream.NegotiatedApplicationProtocol;
#endif

                if (sessionArgs != null) sessionArgs.TimeLine["HTTPS Established"] = DateTime.UtcNow;
            }
        }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x80131620) && retry &&
                                     enabledSslProtocols >= SslProtocols.Tls11)
        {
            stream?.Dispose();
            tcpServerSocket?.Close();

            // Specifying Tls11 and/or Tls12 will disable the usage of Ssl3, even if it has been included.
            // https://docs.microsoft.com/en-us/dotnet/api/system.servicemodel.tcptransportsecurity.sslprotocols?view=dotnet-plat-ext-3.1
            enabledSslProtocols = proxyServer.SupportedSslProtocols & (SslProtocols)0xff;

            if (enabledSslProtocols == SslProtocols.None) throw;

            retry = false;
            goto retry;
        }
        catch (AuthenticationException ex) when (ex.HResult == unchecked((int)0x80131501) && retry &&
                                                 enabledSslProtocols >= SslProtocols.Tls11)
        {
            stream?.Dispose();
            tcpServerSocket?.Close();

            // Specifying Tls11 and/or Tls12 will disable the usage of Ssl3, even if it has been included.
            // https://docs.microsoft.com/en-us/dotnet/api/system.servicemodel.tcptransportsecurity.sslprotocols?view=dotnet-plat-ext-3.1
            enabledSslProtocols = proxyServer.SupportedSslProtocols & (SslProtocols)0xff;

            if (enabledSslProtocols == SslProtocols.None) throw;

            retry = false;
            goto retry;
        }
        catch (Exception)
        {
            stream?.Dispose();
            tcpServerSocket?.Close();
            throw;
        }

        return new TcpServerConnection(proxyServer, tcpServerSocket, stream, remoteHostName, remotePort, isHttps,
            negotiatedApplicationProtocol, httpVersion, externalProxy, upStreamEndPoint, cacheKey);
    }


    /// <summary>
    ///     Release connection back to cache.
    /// </summary>
    /// <param name="connection">The Tcp server connection to return.</param>
    /// <param name="close">Should we just close the connection instead of reusing?</param>
    internal Task Release(TcpServerConnection? connection, bool close = false)
    {
        if (connection == null) return Task.CompletedTask;

        if (close || connection.IsWinAuthenticated || !Server.EnableConnectionPool || connection.IsClosed)
        {
            connection.Dispose();
            return Task.CompletedTask;
        }

        connection.LastAccess = DateTime.UtcNow;

        var existingConnections = cache.GetOrAdd(connection.CacheKey, _ => new ConcurrentQueue<TcpServerConnection>());
        
        while (existingConnections.Count >= Server.MaxCachedConnections)
        {
            if (existingConnections.TryDequeue(out var staleConnection))
                staleConnection.Dispose();
            else
                break;
        }

        if (existingConnections.Any(x => x == connection)) return Task.CompletedTask;

        existingConnections.Enqueue(connection);
        return Task.CompletedTask;
    }

    internal async Task Release(Task<TcpServerConnection?>? connectionCreateTask, bool closeServerConnection)
    {
        if (connectionCreateTask == null) return;

        TcpServerConnection? connection = null;
        try
        {
            connection = await connectionCreateTask.ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }
        finally
        {
            if (connection != null) await Release(connection, closeServerConnection).ConfigureAwait(false);
        }
    }

    private async Task ClearOutdatedConnections()
    {
        while (runCleanUpTask)
            try
            {
                var cutOff = DateTime.UtcNow.AddSeconds(-Server.ConnectionTimeOutSeconds);
                foreach (var item in cache)
                {
                    var queue = item.Value;
                    var initialCount = queue.Count;
                    for (int i = 0; i < initialCount; i++)
                    {
                        if (queue.TryDequeue(out var connection))
                        {
                            if (!Server.EnableConnectionPool || connection.LastAccess < cutOff)
                            {
                                connection.Dispose();
                            }
                            else
                            {
                                queue.Enqueue(connection);
                            }
                        }
                    }
                }

                foreach (var item in cache)
                {
                    if (item.Value.Count == 0)
                    {
                        ((ICollection<KeyValuePair<string, ConcurrentQueue<TcpServerConnection>>>)cache).Remove(item);
                    }
                }
            }
            catch (Exception e)
            {
                Server.ExceptionFunc?.Invoke(new Exception("An error occurred when disposing server connections.", e));
            }
            finally
            {
                // cleanup every 3 seconds by default
                await Task.Delay(3000).ConfigureAwait(false);
            }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed) return;

        runCleanUpTask = false;

        if (disposing)
        {
            foreach (var queue in cache.Select(x => x.Value).ToList())
            {
                while (queue.TryDequeue(out var connection))
                {
                    connection.Dispose();
                }
            }

            cache.Clear();
        }

        disposed = true;
    }

    ~TcpConnectionFactory()
    {
        Dispose(false);
    }

    private static class SocketConnectionTaskFactory
    {
        private static IAsyncResult BeginConnect(IPAddress address, int port, AsyncCallback requestCallback,
            object state)
        {
            return ((Socket)state).BeginConnect(address, port, requestCallback, state);
        }

        private static void EndConnect(IAsyncResult asyncResult)
        {
            ((Socket)asyncResult.AsyncState).EndConnect(asyncResult);
        }

        public static Task CreateTask(Socket socket, IPAddress ipAddress, int port)
        {
            return Task.Factory.FromAsync(BeginConnect, EndConnect, ipAddress, port, socket);
        }
    }

    private static class ProxySocketConnectionTaskFactory
    {
        private static IAsyncResult BeginConnect(IPAddress address, int port, AsyncCallback requestCallback,
            object state)
        {
            return ((ProxySocket.ProxySocket)state).BeginConnect(address, port, requestCallback, state);
        }

        private static IAsyncResult BeginConnect(string hostName, int port, AsyncCallback requestCallback, object state)
        {
            return ((ProxySocket.ProxySocket)state).BeginConnect(hostName, port, requestCallback, state);
        }

        private static void EndConnect(IAsyncResult asyncResult)
        {
            ((ProxySocket.ProxySocket)asyncResult.AsyncState).EndConnect(asyncResult);
        }

        public static Task CreateTask(ProxySocket.ProxySocket socket, IPAddress ipAddress, int port)
        {
            return Task.Factory.FromAsync(BeginConnect, EndConnect, ipAddress, port, socket);
        }

        public static Task CreateTask(ProxySocket.ProxySocket socket, string hostName, int port)
        {
            return Task.Factory.FromAsync(BeginConnect, EndConnect, hostName, port, socket);
        }
    }
}