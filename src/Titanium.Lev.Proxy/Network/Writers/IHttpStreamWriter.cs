using System.Threading;
using System.Threading.Tasks;

namespace Titanium.Lev.Proxy.StreamExtended.Network;

/// <summary>
///     A concrete implementation of this interface is required when calling CopyStream.
/// </summary>
public interface IHttpStreamWriter
{
    bool IsNetworkStream { get; }

    void Write(byte[] buffer, int offset, int count);

    ValueTask WriteAsync(System.ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);

    ValueTask WriteLineAsync(CancellationToken cancellationToken = default);

    ValueTask WriteLineAsync(string value, CancellationToken cancellationToken = default);
}