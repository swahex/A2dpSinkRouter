using A2dpSinkRouter.Bluetooth;

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task MustCancel(Task task)
{
    try { await task; throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { }
}

try
{
    var sessions = new List<FakeSession>();
    await using var controller = new ConnectionController(_ => { var s = new FakeSession(); sessions.Add(s); return s; });
    var states = new List<(string Id, string State)>();
    controller.StateChanged += (id, state) => states.Add((id, state));
    await controller.ConnectAsync("phone-a", CancellationToken.None);
    Require(controller.State("phone-a") == "已连接", "First connection did not open");
    var retired = sessions[0].SnapshotHandler();
    await controller.ConnectAsync("phone-a", CancellationToken.None);
    Require(sessions[0].Disposed && !sessions[1].Disposed, "Reconnect leaked previous session");
    int before = states.Count;
    retired?.Invoke();
    Require(states.Count == before && controller.State("phone-a") == "已连接", "Retired callback changed the new connection");
    await controller.ConnectAsync("phone-b", CancellationToken.None);
    Require(sessions[1].Disposed && controller.State("phone-a") == "未连接", "Switching phone retained old connection");
    await controller.DisconnectAsync("phone-a");
    Require(controller.State("phone-b") == "已连接", "Disconnecting an unrelated phone closed the active session");
    sessions[2].SetState("未连接");
    Require(controller.State("phone-b") == "未连接", "Remote closure was not reflected");
    await controller.DisconnectAsync("phone-b");
    await controller.DisconnectAsync("phone-b");
    Require(sessions[2].DisposeCount == 1, "Disconnect was not idempotent");
    Console.WriteLine("PASS reconnect, phone switch, retired callbacks, remote closure and repeated disconnect");

    var slow = new FakeSession { Wait = new(TaskCreationOptions.RunContinuationsAsynchronously) };
    await using var cancellable = new ConnectionController(_ => slow);
    using var cancellation = new CancellationTokenSource();
    var opening = cancellable.ConnectAsync("phone", cancellation.Token);
    Require(cancellable.State("phone") == "正在连接…", "Missing connecting phase");
    slow.SetState("未连接");
    Require(cancellable.State("phone") == "正在连接…", "Intermediate native Closed event hid the connect operation");
    cancellation.Cancel();
    await MustCancel(opening);
    Require(slow.Disposed && cancellable.State("phone") == "未连接", "Cancelled connection leaked resources");
    Console.WriteLine("PASS connecting phase and cancellation cleanup");

    var failing = new FakeSession { Failure = new IOException("open failed") };
    await using var failures = new ConnectionController(_ => failing);
    try { await failures.ConnectAsync("phone", CancellationToken.None); throw new Exception("Expected open failure"); }
    catch (IOException) { }
    Require(failing.Disposed && failures.State("phone") == "未连接", "Failed open retained a session");
    var closed = new FakeSession { FinishClosed = true };
    await using var closedController = new ConnectionController(_ => closed);
    try { await closedController.ConnectAsync("phone", CancellationToken.None); throw new Exception("Expected closed-at-open failure"); }
    catch (IOException) { }
    Require(closed.Disposed, "Closed-at-open session was not released");
    Console.WriteLine("PASS failed open and closure racing with open completion");

    var delayed = new FakeSession { Wait = new(TaskCreationOptions.RunContinuationsAsynchronously) };
    var disposing = new ConnectionController(_ => delayed);
    using var lifetime = new CancellationTokenSource();
    var connect = disposing.ConnectAsync("phone", lifetime.Token);
    var shutdown = disposing.DisposeAsync().AsTask();
    lifetime.Cancel();
    await MustCancel(connect); await shutdown; await disposing.DisposeAsync();
    Require(delayed.DisposeCount == 1, "Shutdown disposed the connection more than once");
    try { await disposing.ConnectAsync("phone", CancellationToken.None); throw new Exception("Expected disposed controller rejection"); }
    catch (ObjectDisposedException) { }
    Console.WriteLine("PASS shutdown during connect, idempotent disposal and use-after-dispose rejection");

    var held = new FakeSession { Wait = new(TaskCreationOptions.RunContinuationsAsynchronously) };
    int created = 0;
    await using var serialized = new ConnectionController(_ => { created++; return held; });
    var first = serialized.ConnectAsync("first", CancellationToken.None);
    using var queuedCancellation = new CancellationTokenSource();
    var queued = serialized.ConnectAsync("second", queuedCancellation.Token);
    queuedCancellation.Cancel(); await MustCancel(queued);
    Require(created == 1 && !held.Disposed, "Cancelling a queued connect changed the active operation");
    held.Wait.SetResult(); await first;
    Require(serialized.State("first") == "已连接", "Serialized first connect did not complete");
    Console.WriteLine("PASS queued cancellation leaves the active connection attempt intact");

    var brokenClose = new FakeSession { DisposeFailure = true };
    var replacement = new FakeSession(); int attempt = 0;
    await using var resilient = new ConnectionController(_ => attempt++ == 0 ? brokenClose : replacement);
    await resilient.ConnectAsync("phone", CancellationToken.None);
    try { await resilient.DisconnectAsync("phone"); throw new Exception("Expected close failure"); }
    catch (IOException) { }
    Require(resilient.State("phone") == "未连接", "A disposal exception retained stale state");
    await resilient.ConnectAsync("phone", CancellationToken.None);
    Require(resilient.State("phone") == "已连接", "A disposal exception left lifecycle operations locked");
    Console.WriteLine("PASS close failure releases ownership and permits reconnect");
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
return 0;

internal sealed class FakeSession : IPlaybackSession
{
    public event Action? StateChanged;
    public string State { get; private set; } = "未连接";
    public TaskCompletionSource? Wait;
    public Exception? Failure;
    public bool FinishClosed;
    public bool DisposeFailure;
    public int DisposeCount;
    public bool Disposed => DisposeCount > 0;
    public Action? SnapshotHandler() => StateChanged;
    public void SetState(string value) { State = value; StateChanged?.Invoke(); }
    public async Task OpenAsync(CancellationToken cancellation)
    {
        if (Wait is not null) await Wait.Task.WaitAsync(cancellation);
        if (Failure is not null) throw Failure;
        SetState(FinishClosed ? "未连接" : "已连接");
    }
    public void Dispose() { DisposeCount++; if (DisposeFailure) throw new IOException("close failed"); }
}
