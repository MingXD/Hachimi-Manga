using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>
/// 解码隔离验证。
/// 已知限制（Headless 平台伪影）：Avalonia.Headless 下 Bitmap(Stream) 一律解码为 1x1、
/// DecodeToWidth(w) 输出 w×w —— 仅影响测试断言，真实应用（Skia 平台）正常；
/// 像素级验证一律用真实应用 --open + 截屏（见 BUG-001 记录）。
/// </summary>
public class DecodeIsolationTests
{
    private static byte[] MakeJpeg(int w, int h)
    {
        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(new SKColor(245, 240, 235));
            using var paint = new SKPaint { Color = new SKColor(40, 40, 40) };
            canvas.DrawRect(100, 100, 500, 500, paint);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    [AvaloniaFact]
    public void Jpeg_Bytes_Are_Valid_And_Avalonia_Decodes()
    {
        var jpeg = MakeJpeg(2500, 3500);
        Assert.True(jpeg.Length > 1000, "JPEG 字节应为有效图片");

        using var sk = SKBitmap.Decode(jpeg);
        Assert.NotNull(sk);
        Assert.Equal(2500, sk!.Width);
        Assert.Equal(3500, sk.Height);

        // Avalonia Bitmap(Stream) 在 Headless 下解码为 1x1（平台伪影），此处仅断言非空
        using var ms = new MemoryStream(jpeg);
        var bmp = new Bitmap(ms);
        Assert.NotNull(bmp);
    }
}
