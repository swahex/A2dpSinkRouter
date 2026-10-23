using System.Runtime.InteropServices;

namespace A2dpSinkRouter.Audio;

internal sealed record AudioEndpoint(string Id, string Name, int Flow, uint State, Guid? ContainerId, bool? ListenEnabled = null)
{
    public bool IsActive => State == 1;
    public bool IsA2dp => Flow == 1 && Name.Contains("A2DP", StringComparison.OrdinalIgnoreCase);
    public override string ToString() => IsActive ? (IsA2dp ? $"{Name}  [输入就绪]" : Name)
        : $"{Name}  [{(State == 2 ? "已禁用" : Flow == 1 ? "输入未就绪" : "设备未就绪")}]";
}

internal static class EndpointCatalog
{
    public static List<AudioEndpoint> Enumerate()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        var result = new List<AudioEndpoint>();
        try
        {
            for (int flow = 0; flow <= 1; flow++)
            {
                Native.Check(enumerator.EnumAudioEndpoints(flow, 1 | 2 | 8, out var collection));
                try
                {
                    Native.Check(collection.GetCount(out uint count));
                    for (uint i = 0; i < count; i++)
                    {
                        Native.Check(collection.Item(i, out var device));
                        try
                        {
                            Native.Check(device.GetId(out string id));
                            Native.Check(device.GetState(out uint state));
                            Native.Check(device.OpenPropertyStore(0, out var store));
                            try
                            {
                                string? name = ReadProperty(store, new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
                                string? container = ReadProperty(store, new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
                                bool? listen = flow == 1 && state == 1 ? ReadListenEnabled(store) : null;
                                if (name is not null) result.Add(new(id, name, flow, state, Guid.TryParse(container, out var g) ? g : null, listen));
                            }
                            finally { Native.Release(store); }
                        }
                        finally { Native.Release(device); }
                    }
                }
                finally { Native.Release(collection); }
            }
        }
        finally { Native.Release(enumerator); }
        return result.OrderByDescending(e => e.IsActive).ThenBy(e => e.Name).ToList();
    }

    internal static bool? ReadListenEnabled(IPropertyStore store)
    {
        // Best-effort only: this Listen key is not a documented Windows contract.
        // Missing properties, unexpected types and read failures mean unknown.
        // Never write the property or block routing because it cannot be read.
        var key = new PropertyKey { FormatId = new("24dbb0fc-9311-4b3d-9cf0-18ff155639d4"), PropertyId = 1 };
        PropVariant value = default;
        try
        {
            int hr = store.GetValue(ref key, out value);
            return hr >= 0 && value.Type == 11 ? value.Boolean != 0 : null;
        }
        catch (COMException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        finally { Native.PropVariantClear(ref value); }
    }

    private static string? ReadProperty(IPropertyStore store, Guid guid, uint pid)
    {
        var key = new PropertyKey { FormatId = guid, PropertyId = pid };
        if (store.GetValue(ref key, out var value) < 0) return null;
        try
        {
            return value.Type switch
            {
                31 => Marshal.PtrToStringUni(value.Pointer),
                72 when value.Pointer != 0 => Marshal.PtrToStructure<Guid>(value.Pointer).ToString(),
                _ => null
            };
        }
        finally { Native.PropVariantClear(ref value); }
    }
}
