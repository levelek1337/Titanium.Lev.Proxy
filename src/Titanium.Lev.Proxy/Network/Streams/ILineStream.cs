using System.Threading;
using System.Threading.Tasks;

namespace Titanium.Lev.Proxy.StreamExtended.Network;

public interface ILineStream
{
    bool DataAvailable { get; }

    /// <summary>
    ///     Fills the buffer asynchronous.
    /// </summary>
    /// <returns></returns>
    ValueTask<bool> FillBufferAsync(CancellationToken cancellationToken = default);

    byte ReadByteFromBuffer();

    /// <summary>
    ///     Read a line from the byte stream
    /// </summary>
    /// <returns></returns>
    ValueTask<System.ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken = default);
}