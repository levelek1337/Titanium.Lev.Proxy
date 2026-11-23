using System;
using System.Net;
using System.Threading.Tasks;
using Titanium.Lev.Proxy.EventArguments;
using Titanium.Lev.Proxy.Extensions;
using Titanium.Lev.Proxy.Network.WinAuth.Security;

namespace Titanium.Lev.Proxy;

/// <summary>
///     Handle the response from server.
/// </summary>
public partial class ProxyServer
{
    /// <summary>
    ///     Called asynchronously when a request was successful and we received the response.
    /// </summary>
    /// <param name="args">The session event arguments.</param>
    /// <returns> The task.</returns>
    private async Task HandleHttpSessionResponse(SessionEventArgs args)
    {
        var cancellationToken = args.CancellationTokenSource.Token;

        // read response & headers from server
        System.Diagnostics.Debug.WriteLine($"[RESP] ReceiveResponse start: {args.HttpClient.Request.RequestUri}");
        await args.HttpClient.ReceiveResponse(cancellationToken);

        // Server may send expect-continue even if not asked for it in request.
        // According to spec "the client can simply discard this interim response."
        if (args.HttpClient.Response.StatusCode == (int)HttpStatusCode.Continue)
        {
            System.Diagnostics.Debug.WriteLine($"[RESP] Got 100-Continue, clearing and re-receiving");
            await args.ClearResponse(cancellationToken);
            await args.HttpClient.ReceiveResponse(cancellationToken);
        }

        args.TimeLine["Response Received"] = DateTime.UtcNow;

        var response = args.HttpClient.Response;
        args.ReRequest = false;

        System.Diagnostics.Debug.WriteLine($"[RESP] Status={response.StatusCode} HasBody={response.HasBody} IsChunked={response.IsChunked} ContentLength={response.ContentLength} KeepAlive={response.KeepAlive} Uri={args.HttpClient.Request.RequestUri}");

        // check for windows authentication
        if (args.EnableWinAuth)
        {
            if (response.StatusCode == (int)HttpStatusCode.Unauthorized)
                await Handle401UnAuthorized(args);
            else
                WinAuthEndPoint.AuthenticatedResponse(args.HttpClient.Data);
        }

        // save original values so that if user changes them
        // we can still use original values when syphoning out data from attached tcp connection.
        response.SetOriginalHeaders();

        // if user requested call back then do it
        if (!response.Locked)
        {
            System.Diagnostics.Debug.WriteLine($"[RESP] Calling OnBeforeResponse for {args.HttpClient.Request.RequestUri}");
            await OnBeforeResponse(args);
        }

        // it may changed in the user event
        response = args.HttpClient.Response;

        var clientStream = args.ClientStream;

        // user set custom response by ignoring original response from server.
        if (response.Locked)
        {
            System.Diagnostics.Debug.WriteLine($"[RESP] Response.Locked=true — writing custom response, BodyInternal={response.BodyAvailable}");
            // write custom user response with body and return.
            await clientStream.WriteResponseAsync(response, cancellationToken);

            if (args.HttpClient.HasConnection && !args.HttpClient.CloseServerConnection)
                // syphon out the original response body from server connection
                // so that connection will be good to be reused.
                await args.SyphonOutBodyAsync(false, cancellationToken);

            return;
        }

        // if user requested to send request again
        // likely after making modifications from User Response Handler
        if (args.ReRequest)
        {
            System.Diagnostics.Debug.WriteLine($"[RESP] ReRequest=true, retrying");
            if (args.HttpClient.HasConnection) await TcpConnectionFactory.Release(args.HttpClient.Connection);

            // clear current response
            await args.ClearResponse(cancellationToken);
            var result = await HandleHttpSessionRequest(args, null, args.ClientConnection.NegotiatedApplicationProtocol,
                cancellationToken, args.CancellationTokenSource);
            if (result.LatestConnection != null) args.HttpClient.SetConnection(result.LatestConnection);

            return;
        }

        response.Locked = true;

        if (!args.IsTransparent && !args.IsSocks) response.Headers.FixProxyHeaders();

        System.Diagnostics.Debug.WriteLine($"[RESP] WriteResponseAsync headers to client for {args.HttpClient.Request.RequestUri}");
        try
        {
            await clientStream.WriteResponseAsync(response, cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RESP] ERROR WriteResponseAsync: {ex.GetType().Name}: {ex.Message}");
            throw;
        }

        if (response.OriginalHasBody)
        {
            System.Diagnostics.Debug.WriteLine($"[RESP] OriginalHasBody=true IsBodySent={response.IsBodySent} OriginalIsChunked={response.OriginalIsChunked} OriginalContentLength={response.OriginalContentLength}");
            if (response.IsBodySent)
            {
                // syphon out body
                System.Diagnostics.Debug.WriteLine($"[RESP] Body already sent — syphoning out");
                await args.SyphonOutBodyAsync(false, cancellationToken);
            }
            else
            {
                // Copy body if exists
                var serverStream = args.HttpClient.Connection.Stream;
                System.Diagnostics.Debug.WriteLine($"[RESP] Copying body from server to client...");
                try
                {
                    await serverStream.CopyBodyAsync(response, false, clientStream, TransformationMode.None,
                        false, args, cancellationToken);
                    System.Diagnostics.Debug.WriteLine($"[RESP] CopyBodyAsync OK");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RESP] ERROR CopyBodyAsync: {ex.GetType().Name}: {ex.Message}");
                    throw;
                }
            }

            response.IsBodyReceived = true;
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[RESP] No body (OriginalHasBody=false)");
        }

        args.TimeLine["Response Sent"] = DateTime.UtcNow;
        System.Diagnostics.Debug.WriteLine($"[RESP] DONE {args.HttpClient.Request.RequestUri}");
    }

    /// <summary>
    ///     Invoke before response if it is set.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    private async Task OnBeforeResponse(SessionEventArgs args)
    {
        if (BeforeResponse != null) await BeforeResponse.InvokeAsync(this, args, ExceptionFunc);
    }

    /// <summary>
    ///     Invoke after response if it is set.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    private async Task OnAfterResponse(SessionEventArgs args)
    {
        if (AfterResponse != null) await AfterResponse.InvokeAsync(this, args, ExceptionFunc);
    }
#if DEBUG
        internal bool ShouldCallBeforeResponseBodyWrite()
        {
            if (OnResponseBodyWrite != null)
            {
                return true;
            }

            return false;
        }

        internal async Task OnBeforeResponseBodyWrite(BeforeBodyWriteEventArgs args)
        {
            if (OnResponseBodyWrite != null)
            {
                await OnResponseBodyWrite.InvokeAsync(this, args, ExceptionFunc);
            }
        }
#endif
}