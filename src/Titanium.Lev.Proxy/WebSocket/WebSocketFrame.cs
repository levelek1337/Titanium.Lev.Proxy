using System;
using System.Text;

namespace Titanium.Lev.Proxy;

public class WebSocketFrame
{
    public bool IsFinal { get; internal set; }

    public WebsocketOpCode OpCode { get; internal set; }

    public ReadOnlyMemory<byte> Data { get; internal set; }

    public string GetText()
    {
        return GetText(Encoding.UTF8);
    }

    public string GetText(Encoding encoding)
    {
#if NET6_0_OR_GREATER
        return encoding.GetString(Data.Span);
#else
        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(Data, out var segment) && segment.Array != null)
        {
            return encoding.GetString(segment.Array, segment.Offset, segment.Count);
        }
        return encoding.GetString(Data.ToArray());
#endif
    }
}