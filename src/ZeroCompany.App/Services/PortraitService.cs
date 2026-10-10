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

    public PortraitService(string cacheDir) { _cacheDir = cacheDir; }

    /// <summary>Forget in-memory pictures (a different save is open).</summary>
    public void Reset() => _mem.Clear();

    public Bitmap? Get(OpenSave save, string guid)
    {
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
                else if (ExrDecoder.TryDecode(exr, out var w, out var h, out var rgba))
                {
                    bmp = FromRgba(w, h, rgba);
                    try { Directory.CreateDirectory(_cacheDir); bmp.Save(file); } catch (IOException) { }
                }
            }
        }
        catch (Exception e) when (e is IOException or MagickException or ArgumentException or InvalidOperationException) { bmp = null; }
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

/// <summary>EXR to 8-bit sRGB RGBA, matching the original editor's conversion exactly.</summary>
public static class ExrDecoder
{
    public static bool TryDecode(byte[] exr, out int width, out int height, out byte[] rgba)
    {
        width = height = 0; rgba = Array.Empty<byte>();
        try
        {
            using var img = new MagickImage(exr);
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
