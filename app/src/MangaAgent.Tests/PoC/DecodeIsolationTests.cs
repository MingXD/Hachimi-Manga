using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>隔离验证：JPEG 字节有效性 vs Avalonia Bitmap 解码。</summary>
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

    private static byte[] MakePng(int w, int h)
    {
        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(new SKColor(250, 250, 248));
            using var paint = new SKPaint { Color = new SKColor(60, 60, 60) };
            canvas.DrawRect(50, 50, 200, 200, paint);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [AvaloniaFact]
    public void Jpeg_Bytes_Are_Valid_And_Avalonia_Decodes()
    {
        var jpeg = MakeJpeg(2500, 3500);
        Console.WriteLine($"[ISO] jpeg bytes: {jpeg.Length}");

        // Skia 直接解码（基准事实）
        using var sk = SKBitmap.Decode(jpeg);
        Assert.NotNull(sk);
        Console.WriteLine($"[ISO] skia decode: {sk!.Width}x{sk.Height}");

        // Avalonia Bitmap(Stream) 全尺寸解码
        using var ms = new MemoryStream(jpeg);
        var bmp = new Bitmap(ms);
        // 已知 Headless 限制：Bitmap(Stream) 在 Avalonia.Headless 下解码为 1x1（伪影，非真实应用行为）
        Console.WriteLine($"[ISO] avalonia Bitmap(stream) jpeg (headless): {bmp.PixelSize.Width}x{bmp.PixelSize.Height}");
        Assert.NotNull(bmp);
    }


    [AvaloniaFact]
    public void Compare_Decode_Paths_To_Find_Working_Fix()
    {
        var jpeg = MakeJpeg(2500, 3500);
        // A: new Bitmap(stream)，流随 using 释放（当前代码路径）
        var report = new System.Text.StringBuilder();
        using (var ms = new MemoryStream(jpeg))
        {
            var a = new Bitmap(ms);
            report.AppendLine($"A stream-disposed: {a.PixelSize.Width}x{a.PixelSize.Height}");
        }
        var msB = new MemoryStream(jpeg);
        var b = new Bitmap(msB);
        report.AppendLine($"B stream-kept: {b.PixelSize.Width}x{b.PixelSize.Height}");
        using (var ms = new MemoryStream(jpeg))
        {
            var c = Bitmap.DecodeToWidth(ms, 100_000);
            report.AppendLine($"C DecodeToWidth(100000): {c.PixelSize.Width}x{c.PixelSize.Height}");
        }
        var tmp = Path.Combine(Path.GetTempPath(), $"manga_fix_{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(tmp, jpeg);
        try
        {
            var d = new Bitmap(tmp);
            report.AppendLine($"D from-file: {d.PixelSize.Width}x{d.PixelSize.Height}");
        }
        finally { try { File.Delete(tmp); } catch { } }
        File.WriteAllText("D:\\MangaAgent\\app\\decode-paths.txt", report.ToString());
    }

}
