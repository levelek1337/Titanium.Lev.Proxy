using Titanium.Lev.Proxy.Network.Tcp;

namespace Titanium.Lev.Proxy.EventArguments;

public class EmptyProxyEventArgs : ProxyEventArgsBase
{
    internal EmptyProxyEventArgs(ProxyServer server, TcpClientConnection clientConnection) : base(server,
        clientConnection)
    {
    }
}