using System.Runtime.InteropServices;
using A2dpSinkRouter.Audio;

// Fake property stores only: no endpoint activation, sound or system-setting writes.
try
{
    foreach (var (hr, type, value, expected) in new (int, ushort, short, bool?)[]
    {
        (0, 11, -1, true), (0, 11, 1, true), (0, 11, 0, false),
        (0, 0, 0, null), (0, 3, 1, null), (unchecked((int)0x80004005), 11, -1, null)
    })
    {
        var store = new FakeStore { Hr = hr, Type = type, Value = value };
        if (EndpointCatalog.ReadListenEnabled(store) != expected)
            throw new Exception($"Wrong Listen state for hr={hr:X8}, type={type}, value={value}");
    }
    foreach (var error in new Exception[] { new COMException("Unavailable"), new UnauthorizedAccessException() })
    {
        if (EndpointCatalog.ReadListenEnabled(new FakeStore { Error = error }) is not null)
            throw new Exception("Read failure must remain unknown");
    }
    Console.WriteLine("PASS Listen enabled/disabled, missing property, unexpected type, failed HRESULT and read exceptions; no writes");
}
catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }

sealed class FakeStore : IPropertyStore
{
    public int Hr;
    public ushort Type;
    public short Value;
    public Exception? Error;
    public int GetValue(ref PropertyKey key, out PropVariant value)
    {
        if (key.FormatId != new Guid("24dbb0fc-9311-4b3d-9cf0-18ff155639d4") || key.PropertyId != 1)
            throw new Exception("Wrong Listen property key");
        if (Error is not null) throw Error;
        value = new PropVariant { Type = Type, Boolean = Value };
        return Hr;
    }
    public int SetValue(ref PropertyKey key, ref PropVariant value) => throw new Exception("Must not write settings");
    public int Commit() => throw new Exception("Must not commit settings");
    public int GetCount(out uint count) => throw new Exception("Unexpected enumeration");
    public int GetAt(uint index, out PropertyKey key) => throw new Exception("Unexpected enumeration");
}
