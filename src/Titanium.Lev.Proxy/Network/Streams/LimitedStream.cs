using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Titanium.Lev.Proxy.Exceptions;
using Titanium.Lev.Proxy.StreamExtended.BufferPool;
using Titanium.Lev.Proxy.StreamExtended.Network;

namespace Titanium.Lev.Proxy.EventArguments;

internal class LimitedStream : Stream
{
    private static bool TryParseHex(System.ReadOnlySpan<byte> span, out int value)
        {
            value = 0;
            int i = 0;
            while (i < span.Length && (span[i] == 32 || span[i] == 13 || span[i] == 10)) i++;
            int end = span.Length - 1;
            while (end >= i && (span[end] == 32 || span[end] == 13 || span[end] == 10)) end--;
            
            if (i > end) return false;
            
            for (; i <= end; i++)
            {
                var b = span[i];
                int c = -1;
                if (b >= 48 && b <= 57) c = b - 48;
                else if (b >= 97 && b <= 102) c = b - 97 + 10;
                else if (b >= 65 && b <= 70) c = b - 65 + 10;
                if (c == -1) return false;
                value = (value << 4) | c;
            }
            return true;
        }

    private readonly IHttpStreamReader baseReader;
    private readonly IBufferPool bufferPool;
    private readonly bool isChunked;
    private long bytesRemaining;

    private bool readChunkTrail;

    internal LimitedStream(IHttpStreamReader baseStream, IBufferPool bufferPool, bool isChunked,
        long contentLength)
    {
        baseReader = baseStream;
        this.bufferPool = bufferPool;
        this.isChunked = isChunked;
        bytesRemaining = isChunked
            ? 0
            : contentLength == -1
                ? long.MaxValue
                : contentLength;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    private void GetNextChunk()
    {
        if (readChunkTrail)
        {
            var s = baseReader.ReadLineAsync().Result;
            if (s == null)
            {
                bytesRemaining = -1;
                return;
            }
        }

        readChunkTrail = true;

        var chunkHeadMem = baseReader.ReadLineAsync().Result;
        if (chunkHeadMem == null)
        {
            bytesRemaining = -1;
            return;
        }

        var idx = chunkHeadMem.Value.Span.IndexOf((byte)';');
        var chunkLenMem = idx >= 0 ? chunkHeadMem.Value.Slice(0, idx) : chunkHeadMem.Value;

        if (!TryParseHex(chunkLenMem.Span, out var chunkSize))
            throw new ProxyHttpException("Invalid chunk length", null, null);

        bytesRemaining = chunkSize;

        if (chunkSize == 0)
        {
            bytesRemaining = -1;

            var task = baseReader.ReadLineAsync();
            if (!task.IsCompleted)
                task.AsTask().Wait();
        }
    }

    private async Task GetNextChunkAsync()
    {
        if (readChunkTrail)
        {
            var s = await baseReader.ReadLineAsync();
            if (s == null)
            {
                bytesRemaining = -1;
                return;
            }
        }

        readChunkTrail = true;

        var chunkHeadMem = await baseReader.ReadLineAsync();
        if (chunkHeadMem == null)
        {
            bytesRemaining = -1;
            return;
        }

        var idx = chunkHeadMem.Value.Span.IndexOf((byte)';');
        var chunkLenMem = idx >= 0 ? chunkHeadMem.Value.Slice(0, idx) : chunkHeadMem.Value;

        if (!TryParseHex(chunkLenMem.Span, out var chunkSize))
            throw new ProxyHttpException("Invalid chunk length", null, null);

        bytesRemaining = chunkSize;

        if (chunkSize == 0)
        {
            bytesRemaining = -1;
            await baseReader.ReadLineAsync();
        }
    }

    public override void Flush()
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (bytesRemaining == -1) return 0;

        if (bytesRemaining == 0)
        {
            if (isChunked)
                GetNextChunk();
            else
                bytesRemaining = -1;
        }

        if (bytesRemaining == -1) return 0;

        var toRead = (int)Math.Min(count, bytesRemaining);
        var res = baseReader.Read(buffer, offset, toRead);
        bytesRemaining -= res;

        if (res == 0) bytesRemaining = -1;

        return res;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (bytesRemaining == -1) return 0;

        if (bytesRemaining == 0)
        {
            if (isChunked)
                await GetNextChunkAsync();
            else
                bytesRemaining = -1;
        }

        if (bytesRemaining == -1) return 0;

        var toRead = (int)Math.Min(count, bytesRemaining);
        var res = await baseReader.ReadAsync(buffer, offset, toRead, cancellationToken);
        bytesRemaining -= res;

        if (res == 0) bytesRemaining = -1;

        return res;
    }

    public async Task Finish()
    {
        if (bytesRemaining != -1)
        {
            var buffer = bufferPool.GetBuffer();
            try
            {
                var res = await ReadAsync(buffer, 0, buffer.Length);
                if (res != 0) throw new Exception("Data received after stream end");
            }
            finally
            {
                bufferPool.ReturnBuffer(buffer);
            }
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }
}