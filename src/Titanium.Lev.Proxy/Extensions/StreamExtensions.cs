using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Titanium.Lev.Proxy.StreamExtended.BufferPool;

namespace Titanium.Lev.Proxy.Extensions;

/// <summary>
///     Extensions used for Stream and CustomBinaryReader objects
/// </summary>
internal static class StreamExtensions
{
    /// <summary>
    ///     Copy streams asynchronously
    /// </summary>
    /// <param name="input"></param>
    /// <param name="output"></param>
    /// <param name="onCopy"></param>
    /// <param name="bufferPool"></param>
    internal static Task CopyToAsync(this Stream input, Stream output, Action<byte[], int, int> onCopy,
        IBufferPool bufferPool)
    {
        return CopyToAsync(input, output, onCopy, bufferPool, CancellationToken.None);
    }

    /// <summary>
    ///     Copy streams asynchronously
    /// </summary>
    /// <param name="input"></param>
    /// <param name="output"></param>
    /// <param name="onCopy"></param>
    /// <param name="bufferPool"></param>
    /// <param name="cancellationToken"></param>
    internal static async Task CopyToAsync(this Stream input, Stream output, Action<byte[], int, int>? onCopy,
        IBufferPool bufferPool, CancellationToken cancellationToken)
    {
        if (onCopy == null)
        {
            using (cancellationToken.CanBeCanceled ? cancellationToken.Register(() =>
            {
                try { input.Dispose(); } catch { }
                try { output.Dispose(); } catch { }
            }) : default)
            {
                try
                {
                    await input.CopyToAsync(output, bufferPool.BufferSize, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is ObjectDisposedException || ex is IOException || ex is OperationCanceledException)
                {
                }
                return;
            }
        }

        var buffer = bufferPool.GetBuffer();
        try
        {
            using (cancellationToken.CanBeCanceled ? cancellationToken.Register(() =>
            {
                try { input.Dispose(); } catch { }
                try { output.Dispose(); } catch { }
            }) : default)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var bytesRead = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    if (bytesRead != 0 && !cancellationToken.IsCancellationRequested)
                    {
                        await output.WriteAsync(buffer, 0, bytesRead, CancellationToken.None).ConfigureAwait(false);
                        onCopy?.Invoke(buffer, 0, bytesRead);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException || ex is IOException || ex is OperationCanceledException)
        {
            // Ignore cancellation-induced exceptions
        }
        finally
        {
            bufferPool.ReturnBuffer(buffer);
        }
    }
}