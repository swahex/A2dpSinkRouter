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
    var closedSessions = new List<FakeSession>();
    await using var closedController = new ConnectionController(_ =>
    {
        var closed = new FakeSession { FinishClosed = true };
        closedSessions.Add(closed); return closed;
    });
    try { await closedController.ConnectAsync("phone", CancellationToken.None); throw new Exception("Expected closed-at-open failure"); }
    catch (IOException) { }
    Require(closedSessions.Count == 3 && closedSessions.All(s => s.DisposeCount == 1) &&
        closedController.State("phone") == "未连接",
        "Closed-at-open retries were unbounded or leaked a session");
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

    var lateState = new FakeSession { FinishClosed = true };
    int lateCreated = 0;
    await using var settling = new ConnectionController(_ => { lateCreated++; return lateState; });
    var settlingConnect = settling.ConnectAsync("phone", CancellationToken.None);
    Require(!settlingConnect.IsCompleted && settling.State("phone") == "正在连接…",
        "Open success with a delayed state update was rejected immediately");
    lateState.SetState("已连接");
    await settlingConnect;
    Require(lateCreated == 1 && !lateState.Disposed && settling.State("phone") == "已连接",
        "Delayed Opened state caused unnecessary teardown");
    Console.WriteLine("PASS delayed native Opened state settles without replacing the session");

    var neverSettled = new FakeSession { FinishClosed = true };
    await using var settlingCancellation = new ConnectionController(_ => neverSettled);
    using var settlingCancel = new CancellationTokenSource();
    var settlingCancelled = settlingCancellation.ConnectAsync("phone", settlingCancel.Token);
    settlingCancel.Cancel(); await MustCancel(settlingCancelled);
    Require(neverSettled.DisposeCount == 1 && settlingCancellation.State("phone") == "未连接",
        "Cancelling state settlement retained a native session");
    Console.WriteLine("PASS cancellation during native state settlement releases the session");

    int publishingCreated = 0, reads = 0;
    var publishingRace = new FakeSession { StateReader = () => ++reads == 1 ? "已连接" : "未连接" };
    await using var publishing = new ConnectionController(_ => ++publishingCreated == 1 ? publishingRace : new FakeSession());
    await publishing.ConnectAsync("phone", CancellationToken.None);
    Require(publishingCreated == 2 && publishingRace.DisposeCount == 1 && publishing.State("phone") == "已连接",
        "Closure between state settlement and success publication was reported as success");
    Console.WriteLine("PASS closure before success publication retries rather than reporting false success");

    var recoveringSessions = new List<FakeSession>();
    var recoveryStates = new List<string>();
    long releasedAt = 0;
    await using var recovering = new ConnectionController(_ =>
    {
        if (recoveringSessions.Count > 0)
            Require(System.Diagnostics.Stopwatch.GetElapsedTime(releasedAt).TotalMilliseconds >= 1400,
                "Replacement was created before the native transport cooldown");
        var session = new FakeSession { FinishClosed = recoveringSessions.Count == 0 };
        session.OnDispose = () => releasedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        recoveringSessions.Add(session); return session;
    });
    recovering.StateChanged += (_, state) => recoveryStates.Add(state);
    var recovery = recovering.ConnectAsync("phone", CancellationToken.None);
    var failedCallback = recoveringSessions[0].SnapshotHandler();
    await recovery;
    int recoveryBefore = recoveryStates.Count;
    failedCallback?.Invoke();
    Require(recoveringSessions.Count == 2 && recoveringSessions[0].DisposeCount == 1 &&
        !recoveringSessions[1].Disposed && recovering.State("phone") == "已连接",
        "Success-then-closed race did not recover with a fresh owned session");
    Require(recoveryStates.SequenceEqual(new[] { "正在连接…", "已连接" }) && recoveryStates.Count == recoveryBefore,
        "Retry or retired callback published a false disconnected state");
    Console.WriteLine("PASS success-then-closed retry waits for release and ignores retired callbacks");

    await recovering.DisconnectAsync("phone");
    using var cooldownCancel = new CancellationTokenSource();
    var cooldownConnect = recovering.ConnectAsync("phone", cooldownCancel.Token);
    Require(recoveringSessions.Count == 2 && recovering.State("phone") == "正在连接…",
        "Manual disconnect allowed immediate native recreation or hid the connecting phase");
    cooldownCancel.Cancel(); await MustCancel(cooldownConnect);
    Require(recoveringSessions.Count == 2 && recovering.State("phone") == "未连接",
        "Cancelling the reconnect cooldown created a session or retained connecting state");
    Console.WriteLine("PASS manual disconnect cooldown is cancellable before creating the next session");

    var retryReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    int retryCreated = 0;
    await using var retryCancellation = new ConnectionController(_ =>
    {
        retryCreated++;
        return new FakeSession { FinishClosed = true, OnDispose = () => retryReleased.TrySetResult() };
    });
    using var retryCancel = new CancellationTokenSource();
    var retryConnect = retryCancellation.ConnectAsync("phone", retryCancel.Token);
    await retryReleased.Task.WaitAsync(TimeSpan.FromSeconds(5));
    retryCancel.Cancel(); await MustCancel(retryConnect);
    Require(retryCreated == 1 && retryCancellation.State("phone") == "未连接",
        "Cancelling retry backoff started another native attempt");
    Console.WriteLine("PASS cancellation during retry backoff prevents further attempts");

    var nativeError = new System.Runtime.InteropServices.COMException("native state read failed", unchecked((int)0x80070005));
    int nativeCreated = 0;
    var unreadable = new FakeSession { StateFailure = nativeError };
    await using var diagnostics = new ConnectionController(_ => { nativeCreated++; return unreadable; });
    try { await diagnostics.ConnectAsync("phone", CancellationToken.None); throw new Exception("Expected native state failure"); }
    catch (System.Runtime.InteropServices.COMException ex)
    {
        Require(ReferenceEquals(ex, nativeError) && ex.HResult == unchecked((int)0x80070005), "Native HRESULT was replaced");
    }
    Require(nativeCreated == 1 && unreadable.DisposeCount == 1 && diagnostics.State("phone") == "未连接",
        "Native state error was retried or leaked ownership");
    Console.WriteLine("PASS native state errors preserve diagnostics and do not trigger retries");
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
    public Exception? StateFailure;
    public Func<string>? StateReader;
    public Action? OnDispose;
    public int DisposeCount;
    public bool Disposed => DisposeCount > 0;
    public Action? SnapshotHandler() => StateChanged;
    public string ReadState() => StateFailure is null ? StateReader?.Invoke() ?? State : throw StateFailure;
    public void SetState(string value) { State = value; StateChanged?.Invoke(); }
    public async Task OpenAsync(CancellationToken cancellation)
    {
        if (Wait is not null) await Wait.Task.WaitAsync(cancellation);
        if (Failure is not null) throw Failure;
        SetState(FinishClosed ? "未连接" : "已连接");
    }
    public void Dispose() { DisposeCount++; OnDispose?.Invoke(); if (DisposeFailure) throw new IOException("close failed"); }
}
