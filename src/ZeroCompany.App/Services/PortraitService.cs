using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ImageMagick;
using ZeroCompany.Core.Save;

namespace ZeroCompany.App.Services;

/// <summary>
/// Operator portraits are OpenEXR pictures (half-float RGBA, DWAA-compressed) inside the save. We decode them once, convert
/// linear light to sRGB, and cache the PNG on disk keyed by the picture's hash.
/// </summary>
public sealed class PortraitService
{
    readonly string _cacheDir;
    readonly Dictionary<string, Bitmap?> _mem = new();
    /// <summary>Magick's native library could not be loaded (Magick.NET has none for Intel Macs): show initials, don't retry.</summary>
    bool _noDecoder;

    public PortraitService(string cacheDir) { _cacheDir = cacheDir; }

    /// <summary>Show initials instead of saved portraits (set ZC_PORTRAITS=off; used for the README screenshots).</summary>
    public bool Enabled { get; set; } = !string.Equals(Environment.GetEnvironmentVariable("ZC_PORTRAITS"), "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>Forget in-memory pictures (a different save is open).</summary>
    public void Reset() => _mem.Clear();

    public Bitmap? Get(OpenSave save, string guid)
    {
        if (!Enabled) return null;
        guid = guid.ToUpperInvariant();
        var key = save.Path + "|" + guid;
        if (_mem.TryGetValue(key, out var hit)) return hit;
        Bitmap? bmp = null;
        try
        {
            var exr = save.Portraits.ExrBytes(guid);
            if (exr != null)
            {
                var file = Path.Combine(_cacheDir, Convert.ToHexString(SHA1.HashData(exr)).ToLowerInvariant() + ".png");
                if (File.Exists(file)) bmp = new Bitmap(file);
                else if (!_noDecoder && ExrDecoder.TryDecode(exr, out var w, out var h, out var rgba))
                {
                    bmp = FromRgba(w, h, rgba);
                    try { Directory.CreateDirectory(_cacheDir); bmp.Save(file); } catch (IOException) { }
                }
            }
        }
        catch (Exception e) when (e is IOException or MagickException or ArgumentException or InvalidOperationException) { bmp = null; }
        catch (Exception e) when (e is TypeInitializationException or DllNotFoundException) { _noDecoder = true; bmp = null; }
        _mem[key] = bmp;
        return bmp;
    }

    static WriteableBitmap FromRgba(int w, int h, byte[] rgba)
    {
        var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var fb = bmp.Lock();
        for (int y = 0; y < h; y++) Marshal.Copy(rgba, y * w * 4, fb.Address + y * fb.RowBytes, w * 4);
        return bmp;
    }
}

/// <summary>
/// EXR to 8-bit sRGB RGBA, matching the original editor's conversion exactly. The bytes come from a save file, which may be
/// someone else's, so only the OpenEXR decoder is ever used (never format auto-detection) and the picture size is capped.
/// </summary>
public static class ExrDecoder
{
    /// <summary>Longest side accepted; portraits are a few hundred pixels across.</summary>
    public const int MaxSide = 4096;

    static readonly byte[] ExrMagic = { 0x76, 0x2f, 0x31, 0x01 };

    static ExrDecoder()
    {
        ResourceLimits.Width = MaxSide;
        ResourceLimits.Height = MaxSide;
        ResourceLimits.Memory = 1UL << 30;
    }

    public static bool TryDecode(byte[] exr, out int width, out int height, out byte[] rgba)
    {
        width = height = 0; rgba = Array.Empty<byte>();
        if (exr.Length < ExrMagic.Length || !exr.AsSpan(0, ExrMagic.Length).SequenceEqual(ExrMagic)) return false;
        try
        {
            using var img = new MagickImage(exr, new MagickReadSettings { Format = MagickFormat.Exr });
            width = (int)img.Width; height = (int)img.Height;
            var px = img.GetPixels().ToArray();
            if (px == null) return false;
            int ch = (int)img.ChannelCount;
            if (ch < 3) return false;
            rgba = ToSrgb8(px, width * height, ch, Quantum.Max);
            return true;
        }
        catch (MagickException) { return false; }
    }

    /// <summary>Clamp linear values to 0..1 and apply the sRGB transfer curve; alpha stays linear.</summary>
    public static byte[] ToSrgb8(IReadOnlyList<float> px, int pixels, int channels, double scale)
    {
        var rgba = new byte[pixels * 4];
        for (int i = 0; i < pixels; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                double v = Math.Clamp(px[i * channels + c] / scale, 0, 1);
                double s = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
                rgba[i * 4 + c] = (byte)(s * 255 + 0.5);
            }
            double a = channels >= 4 ? Math.Clamp(px[i * channels + 3] / scale, 0, 1) : 1;
            rgba[i * 4 + 3] = (byte)(a * 255 + 0.5);
        }
        return rgba;
    }
}
