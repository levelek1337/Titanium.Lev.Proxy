using System;
using Titanium.Lev.Proxy.Models;

namespace Titanium.Lev.Proxy.Helpers;

internal struct RequestStatusInfo
{
    public string Method { get; set; }

    public ByteString RequestUri { get; set; }

    public Version Version { get; set; }

    public bool IsEmpty()
    {
        return Method == null && RequestUri.Length == 0 && Version == null;
    }
}