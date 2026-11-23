using System;
using System.Threading;
using System.Threading.Tasks;
using Titanium.Lev.Proxy.Models;
using Titanium.Lev.Proxy.StreamExtended.Network;

namespace Titanium.Lev.Proxy.Http;

internal static class HeaderParser
{
    internal static async ValueTask ReadHeaders(ILineStream reader, HeaderCollection headerCollection,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var lineMem = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (lineMem == null || lineMem.Value.Length == 0)
            {
                break; // end of headers
            }

            ProcessHeaderLine(lineMem.Value.Span, headerCollection);
        }
    }

    private static void ProcessHeaderLine(System.ReadOnlySpan<byte> buffer, Titanium.Lev.Proxy.Http.HeaderCollection headerCollection)
    {
        int colonIndex = buffer.IndexOf((byte)':');
        if (colonIndex == -1) return; // skip malformed/blank lines silently (don't kill connection)

        var nameSpan = buffer.Slice(0, colonIndex);

        int valueStart = colonIndex + 1;
        while (valueStart < buffer.Length && buffer[valueStart] == ' ')
        {
            valueStart++;
        }

        var valueSpan = buffer.Slice(valueStart);

        var knownName = Titanium.Lev.Proxy.Http.KnownHeaders.GetKnownHeader(nameSpan);
        if (knownName != null)
        {
            var knownValue = Titanium.Lev.Proxy.Http.KnownHeaders.GetKnownHeader(valueSpan);
            if (knownValue != null)
            {
                headerCollection.AddHeader(new Titanium.Lev.Proxy.Models.HttpHeader(knownName, knownValue));
            }
            else
            {
                var valueArray = new byte[valueSpan.Length];
                valueSpan.CopyTo(valueArray);
                headerCollection.AddHeader(new Titanium.Lev.Proxy.Models.HttpHeader(knownName.String8, new Titanium.Lev.Proxy.Models.ByteString(valueArray)));
            }
        }
        else
        {
            var nameArray = new byte[nameSpan.Length];
            nameSpan.CopyTo(nameArray);
            var valueArray = new byte[valueSpan.Length];
            valueSpan.CopyTo(valueArray);
            headerCollection.AddHeader(new Titanium.Lev.Proxy.Models.HttpHeader(new Titanium.Lev.Proxy.Models.ByteString(nameArray), new Titanium.Lev.Proxy.Models.ByteString(valueArray)));
        }
    }
}