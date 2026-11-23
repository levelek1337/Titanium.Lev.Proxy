using System.Net;

namespace Titanium.Lev.Proxy.Http.Responses;

/// <summary>
///     The http 200 Ok response.
/// </summary>
public sealed class OkResponse : Response
{
    /// <summary>
    ///     Constructor.
    /// </summary>
    public OkResponse()
    {
        StatusCode = (int)HttpStatusCode.OK;
        StatusDescription = "OK";
    }

    /// <summary>
    ///     Constructor.
    /// </summary>
    public OkResponse(Microsoft.IO.RecyclableMemoryStream bodyStream) : this()
    {
        BodyStream = bodyStream;
    }
}