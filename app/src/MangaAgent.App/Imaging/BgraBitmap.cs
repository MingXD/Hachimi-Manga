using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MangaAgent.App.Imaging;

/// <summary>把原生 BGRA8888 像素缓冲包成 Avalonia 位图（PDF 光栅输出直通，避免编码往返）。
/// WriteableBitmap 为平台实现（Skia/Headless 均可用），按行拷贝到锁定的帧缓冲。</summary>
public static class BgraBitmap
{
    public static Bitmap Create(int width, int height, byte[] bgra)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
            throw new ArgumentException("非法的 BGRA 缓冲尺寸");
        var wb = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        using var fb = wb.Lock();
        int rowBytes = Math.Min(width * 4, fb.RowBytes);
        for (int row = 0; row < height; row++)
        {
            Marshal.Copy(bgra, row * width * 4,
                IntPtr.Add(fb.Address, row * fb.RowBytes), rowBytes);
        }
        return wb;
    }
}
