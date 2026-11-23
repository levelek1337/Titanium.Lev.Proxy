## Titanium Lev Proxy

> **Titanium.Lev.Proxy** is a high-performance fork of [Titanium.Web.Proxy](https://github.com/justcoding121/titanium-web-proxy) 3.2.0. It replaces conventional raw `byte[]` allocations and standard streams with **`Microsoft.IO.RecyclableMemoryStream`** pool management and drastically cuts both **CPU** and **RAM** usage.
>
> ### Why this fork?
> - Unlike the official titanium proxy which got unarchived very recently or most popular fork [unobtanium-web-proxy](https://github.com/svrooij/unobtanium-web-proxy) i still support old .NET Framework. and code is almost fully backwards compatible with original 3.2.0 titanium proxy
> - **Drastically Lower RAM, CPU & GC Overhead:** Pools and recycles memory streams (`RecyclableMemoryStream`), rewrote HTTP header/chunk parsing on `ReadOnlySpan<byte>` / `ReadOnlyMemory<byte>` and `ValueTask`, and eliminated LOH fragmentation and CPU-heavy Gen 0/1/2 GC pauses.
> - **Fixed BouncyCastle Certificate Generation (Massive CPU Drop):** Original Titanium generated a brand-new RSA 2048 key pair and certificate from scratch for every single domain, spiking CPU to ~50%+. This fork generates one RSA key in RAM (`BouncyCastleFast` by default) and reuses it, only changing who the certificate belongs to (the target domain/subject) when signing site certs in ~1-2 ms, dropping CPU usage down to ~2%.
> - **Proper Resource & Memory Disposal:** Fixed memory and handle leaks across the proxy - immediate `Dispose()` of closed TCP connections (removed delayed disposal bags), proper `Dispose()` of expired/cached `X509Certificate2` handles, and automatic stream cleanup at session end (with `ExternallyOwned` flag support).
> - **Zstd Support & Low-CPU Re-Compression:** Added Zstandard (`zstd`) support, tuned fast compression levels for Brotli/Zstd to prevent CPU bottlenecks on large payloads, and fixed `Content-Length` / `Transfer-Encoding: chunked` handling when reading or modifying bodies.
> - **Faster Networking:** Removed redundant `Task.Run` thread-pool scheduling, replaced blocking `socket.Send` kernel transitions with lightweight non-blocking `socket.Poll`, improved 1:1 Keep-Alive connection reuse.

> - & other small improvements


---

### How to use?

replace original titanium proxy 3.2.0 with Titanium.Lev.Proxy.dll in refernces, rename usings.

- change how u read bodys of requests/responses, example:
  ```csharp
  Microsoft.IO.RecyclableMemoryStream body = await Task.Run(async () => await e.GetResponseBodyStream(true));
  ```
- **Decompressed vs Compressed body (`true` / `false`):**
  You can pass a `bool decompress` parameter to `GetResponseBodyStream(bool)`, `GetResponseBodyAsString(bool)`, `GetRequestBodyStream(bool)`, and `GetRequestBodyAsString(bool)`:
  - `true` *(default)* - returns **decompressed** body (automatically unpacks `gzip`, `deflate`, `br`, `zstd`).
  - `false` - returns raw **compressed** body without decompressing (faster and saves CPU if you don't need to inspect/modify the plain text).
- if u stored requests/responses as `byte[]` later u might need a little other changes like `.ToArray()` but other than that u dont need to do more.

### Supported Frameworks

* .NET 6.0 or above
* .NET Framework 4.6.2 or above

### License

Licensed under the **Titanium.Lev.Proxy License** - you are free to use, edit, modify, and redistribute it in source or binary form, **provided that this license and credit to `levelek1337` are preserved**. Based on original `Titanium.Web.Proxy` (MIT License). See [LICENSE](LICENSE) for details.

### Minimal Requirements

list of .dlls you need to load locally for Titanium.Lev.Proxy to work on .NET 4.8 (i didnt tested .NET 6.0+ at all but it should work fine)

```text
├── BouncyCastle.Crypto.dll
├── BrotliSharpLib.dll
├── ZstdSharp.dll
├── Microsoft.IO.RecyclableMemoryStream.dll
├── Newtonsoft.Json.dll
├── System.Buffers.dll
├── System.Memory.dll
├── System.Numerics.Vectors.dll
├── System.Runtime.CompilerServices.Unsafe.dll
├── System.Threading.Tasks.Extensions.dll
```

on lower than 4.7.2 u might need to locally copy System.Security.AccessControl.dll too


# by levelek1337
