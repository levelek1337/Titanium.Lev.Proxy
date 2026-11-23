using System;
using System.Threading;
using System.Threading.Tasks;
using Titanium.Lev.Proxy.StreamExtended.Network;

namespace Titanium.Lev.Proxy.Helpers;

internal class NullWriter : IHttpStreamWriter
{
    private NullWriter()
    {
    }

    public static NullWriter Instance { get; } = new();

    public bool IsNetworkStream => false;

    public void Write(byte[] buffer, int offset, int count)
    {
    }

    public ValueTask WriteAsync(System.ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        return default;
    }

    public ValueTask WriteLineAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public ValueTask WriteLineAsync(string value, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}