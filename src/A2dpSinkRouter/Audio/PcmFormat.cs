using System.Runtime.InteropServices;

namespace A2dpSinkRouter.Audio;

internal sealed record PcmFormat(int Rate, int Channels, int Bits, int BlockAlign, bool FloatingPoint)
{
    private static readonly Guid Pcm = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid Float = new("00000003-0000-0010-8000-00aa00389b71");
    public int BytesPerSample => Bits / 8;
    public static PcmFormat Read(nint pointer)
    {
        int tag = (ushort)Marshal.ReadInt16(pointer, 0);
        int channels = (ushort)Marshal.ReadInt16(pointer, 2);
        int rate = Marshal.ReadInt32(pointer, 4);
        int align = (ushort)Marshal.ReadInt16(pointer, 12);
        int bits = (ushort)Marshal.ReadInt16(pointer, 14);
        bool floating = tag == 3;
        if (tag == 0xfffe)
        {
            if ((ushort)Marshal.ReadInt16(pointer, 16) < 22) throw new NotSupportedException("无效的扩展音频格式。");
            var subtype = Marshal.PtrToStructure<Guid>(pointer + 24);
            if (subtype != Pcm && subtype != Float) throw new NotSupportedException("端点不是 PCM/IEEE float 格式。");
            floating = subtype == Float;
        }
        else if (tag != 1 && tag != 3) throw new NotSupportedException("不支持此端点的压缩音频格式。");
        if (rate < 8000 || rate > 384000 || channels < 1 || channels > 32 || align != channels * (bits / 8) ||
            (floating ? bits is not (32 or 64) : bits is not (8 or 16 or 24 or 32)))
            throw new NotSupportedException("不支持此端点的采样率、声道布局或位深。");
        if (tag == 0xfffe && channels > 2)
        {
            uint mask = (uint)Marshal.ReadInt32(pointer, 20);
            if ((mask & 3) != 3) throw new NotSupportedException("多声道输出必须包含前置左/右声道。");
        }
        return new(rate, channels, bits, align, floating);
    }

    public override string ToString() => $"{Rate / 1000.0:0.#} kHz / {Channels} ch / {(FloatingPoint ? "float" : "PCM")}{Bits}";

    public unsafe float Decode(byte* sample)
    {
        float value;
        if (FloatingPoint) value = Bits == 32 ? *(float*)sample : (float)*(double*)sample;
        else value = Bits switch
        {
            8 => (*sample - 128) / 128f,
            16 => *(short*)sample / 32768f,
            24 => ((sample[0] | sample[1] << 8 | sample[2] << 16) << 8 >> 8) / 8388608f,
            32 => *(int*)sample / 2147483648f,
            _ => 0
        };
        return float.IsFinite(value) ? value : 0;
    }

    public unsafe void Encode(byte* sample, float value)
    {
        value = float.IsFinite(value) ? Math.Clamp(value, -1f, 1f) : 0;
        if (FloatingPoint)
        {
            if (Bits == 32) *(float*)sample = value;
            else *(double*)sample = value;
        }
        else switch (Bits)
        {
            case 8: *sample = (byte)Math.Clamp((int)Math.Round(value * 127 + 128), 0, 255); break;
            case 16: *(short*)sample = (short)Math.Round(value * 32767); break;
            case 24:
                int v = (int)Math.Round(value * 8388607);
                sample[0] = (byte)v; sample[1] = (byte)(v >> 8); sample[2] = (byte)(v >> 16);
                break;
            case 32: *(int*)sample = (int)Math.Round(value * 2147483647.0); break;
        }
    }
}
