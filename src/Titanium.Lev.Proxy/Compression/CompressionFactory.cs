using System;
using System.IO;
using System.IO.Compression;

namespace Titanium.Lev.Proxy.Compression
{
    /// <summary>
    ///     A factory to generate the compression methods based on the type of compression
    /// </summary>
    internal static class CompressionFactory
    {
        internal static Stream Create(HttpCompression type, Stream stream, bool leaveOpen = true)
        {
            return type switch
            {
                HttpCompression.Gzip => new GZipStream(stream, CompressionMode.Compress, leaveOpen),
                HttpCompression.Deflate => new DeflateStream(stream, CompressionMode.Compress, leaveOpen),
                HttpCompression.Brotli => CreateBrotliStream(stream, leaveOpen),
                HttpCompression.Zstd => new ZstdSharp.CompressionStream(leaveOpen ? new NonClosingStream(stream) : stream, level: 1, bufferSize: 0, leaveOpen: false),
                _ => throw new Exception($"Unsupported compression mode: {type}")
            };
        }

        private static Stream CreateBrotliStream(Stream stream, bool leaveOpen)
        {
            var br = new BrotliSharpLib.BrotliStream(stream, CompressionMode.Compress, leaveOpen);
            br.SetQuality(1); // 1 = fastest, 11 = best/slowest
            return br;
        }
    }

    internal class NonClosingStream : Stream
    {
        private readonly Stream _stream;
        public NonClosingStream(Stream stream) { _stream = stream; }
        public override bool CanRead => _stream.CanRead;
        public override bool CanSeek => _stream.CanSeek;
        public override bool CanWrite => _stream.CanWrite;
        public override long Length => _stream.Length;
        public override long Position { get => _stream.Position; set => _stream.Position = value; }
        public override void Flush() => _stream.Flush();
        public override System.Threading.Tasks.Task FlushAsync(System.Threading.CancellationToken cancellationToken) => _stream.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);
        public override System.Threading.Tasks.Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken) => _stream.ReadAsync(buffer, offset, count, cancellationToken);
#if NET6_0_OR_GREATER
        public override System.Threading.Tasks.ValueTask<int> ReadAsync(System.Memory<byte> buffer, System.Threading.CancellationToken cancellationToken = default) => _stream.ReadAsync(buffer, cancellationToken);
        public override System.Threading.Tasks.ValueTask WriteAsync(System.ReadOnlyMemory<byte> buffer, System.Threading.CancellationToken cancellationToken = default) => _stream.WriteAsync(buffer, cancellationToken);
#endif
        public override long Seek(long offset, System.IO.SeekOrigin origin) => _stream.Seek(offset, origin);
        public override void SetLength(long value) => _stream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _stream.Write(buffer, offset, count);
        public override System.Threading.Tasks.Task WriteAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken) => _stream.WriteAsync(buffer, offset, count, cancellationToken);
        protected override void Dispose(bool disposing) { /* do not dispose _stream */ }
    }
}