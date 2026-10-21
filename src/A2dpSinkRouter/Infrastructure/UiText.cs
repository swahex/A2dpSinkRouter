using System.Globalization;

namespace A2dpSinkRouter.Infrastructure;

internal sealed record LocalizedText(string Chinese, string English)
{
    public override string ToString() => UiText.T(Chinese, English);
}

internal static class UiText
{
    public static string Language { get; set; } = "zh-CN";
    public static bool English => Language == "en-US";
    public static string T(string chinese, string english) => English ? english : chinese;
    public static string F(string chinese, string english, params object?[] values) =>
        string.Format(English ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("zh-CN"), T(chinese, english), values);
    public static string Connection(string value) => !English ? value : value switch
    {
        "已连接" => "Connected", "未连接" => "Disconnected", "等待连接" => "Waiting for connection",
        "正在连接…" => "Connecting…", "连接已关闭" => "Connection closed", _ => Engine(value)
    };
    public static string Engine(string value)
    {
        if (!English) return value;
        // Hardware/device names remain untouched. These phrases belong to our engine.
        foreach (var pair in EngineTerms) value = value.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        return value;
    }
    private static readonly KeyValuePair<string, string>[] EngineTerms =
    [
        new("不支持此端点的采样率、声道布局或位深。", "Unsupported sample rate, channel layout, or bit depth."),
        new("设备不支持当前采样率的独占输出格式，请关闭独占选项后重试。", "Exclusive output is unavailable at this sample rate. Try shared output."),
        new("音频设备未响应事件，路由已停止。请检查设备连接后重新启动。", "The audio device stopped responding. Check its connection and restart the route."),
        new("无法为此设备创建 A2DP 接收连接，请先在 Windows 中完成蓝牙配对。", "Cannot create an A2DP connection. Pair this device in Windows first."),
        new("首版输入支持单声道或立体声，请选择 A2DP 输入端点。", "Select a mono or stereo A2DP input."),
        new("音频端点尚未就绪，正在等待设备可用。", "Waiting for the audio device to become ready."),
        new("采集端点返回了无效的非静音数据指针。", "The capture device returned an invalid audio buffer."),
        new("输入或输出端点方向错误。", "Invalid input or output device direction."),
        new("端点不是 PCM/IEEE float 格式。", "The device format is not PCM/IEEE float."),
        new("不支持此端点的压缩音频格式。", "Compressed device formats are not supported."),
        new("多声道输出必须包含前置左/右声道。", "Multichannel output requires front left/right channels."),
        new("无效的扩展音频格式。", "Invalid extensible audio format."),
        new("共享兼容 (低周期 ", "Shared fallback (low-period "),
        new("独占事件驱动", "Exclusive"), new("共享低周期", "Low-period shared"), new("共享兼容", "Shared"),
        new("路由已停止：", "Route stopped: "), new("路由已停止", "Route stopped"),
        new("端点缓冲：输入 ", "Device buffers: input "), new("，输出 ", ", output "),
        new("输入 ", "Input "), new("输出 ", "Output "), new(" 帧", " frames"),
        new("MMCSS 不可用", "MMCSS unavailable"), new("蓝牙连接失败：", "Bluetooth connection failed: ")
    ];
}
