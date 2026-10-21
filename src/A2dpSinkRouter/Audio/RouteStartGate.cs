namespace A2dpSinkRouter.Audio;

internal sealed record RouteOptions(string InputId, string OutputId, int BufferMs, bool ExclusiveOutput = false);
internal enum RouteReadiness { NoRequest, WaitingForInput, WaitingForOutput, Ready }

// A click records intent. Endpoint availability can arrive later, independently of
// Bluetooth connection completion. Cancellation removes the pending intent.
internal sealed class RouteStartGate
{
    public RouteOptions? Requested { get; private set; }
    public void Request(RouteOptions options) => Requested = options;
    public void Cancel() => Requested = null;
    public RouteReadiness Evaluate(Func<string, bool> isAvailable)
    {
        var request = Requested;
        if (request is null) return RouteReadiness.NoRequest;
        if (!isAvailable(request.InputId)) return RouteReadiness.WaitingForInput;
        if (!isAvailable(request.OutputId)) return RouteReadiness.WaitingForOutput;
        return RouteReadiness.Ready;
    }
    public bool Complete(RouteOptions request)
    {
        if (!ReferenceEquals(Requested, request)) return false;
        Requested = null;
        return true;
    }
}

internal sealed class AudioEndpointNotReadyException(string message) : IOException(message);
