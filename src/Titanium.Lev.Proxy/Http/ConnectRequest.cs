using Titanium.Lev.Proxy.Models;
using Titanium.Lev.Proxy.StreamExtended;

namespace Titanium.Lev.Proxy.Http;

/// <summary>
///     The tcp tunnel Connect request.
/// </summary>
public class ConnectRequest : Request
{
    internal ConnectRequest(ByteString authority)
    {
        Method = "CONNECT";
        Authority = authority;
    }

    public TunnelType TunnelType { get; internal set; }

    public ClientHelloInfo? ClientHelloInfo { get; set; }
}