using System;
using Titanium.Lev.Proxy.Extensions;
using Titanium.Lev.Proxy.Models;

namespace Titanium.Lev.Proxy.Http;

public class KnownHeader
{
    public string String;
    internal ByteString String8;

    private KnownHeader(string str)
    {
        String8 = (ByteString)str;
        String = str;
    }

    public override string ToString()
    {
        return String;
    }

    internal bool Equals(ReadOnlySpan<char> value)
    {
        return String.AsSpan().EqualsIgnoreCase(value);
    }

    internal bool Equals(string? value)
    {
        return String.EqualsIgnoreCase(value);
    }

    public static implicit operator KnownHeader(string str)
    {
        return new(str);
    }
}