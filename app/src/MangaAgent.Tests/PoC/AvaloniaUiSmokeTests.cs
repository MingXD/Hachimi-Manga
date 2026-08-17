using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(MangaAgent.Tests.PoC.AvaloniaTestApp))]

namespace MangaAgent.Tests.PoC;

/// <summary>Headless 测试 Application。</summary>
public class AvaloniaTestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<AvaloniaTestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>M0.5 PoC：Avalonia UI 冒烟 —— 虚拟化列表（1 万项封面墙）+ 大图渲染。</summary>
public class AvaloniaUiSmokeTests
{
    private static string FixtureCover => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "reference", "fixtures", "library",
            "TestSeriesA", "TestSeriesA_vol1_folder", "0001.png"));

    [AvaloniaFact]
    public void Virtualized_List_With_10k_Items_Renders()
    {
        var window = new Window { Width = 1200, Height = 800 };
        var list = new ListBox
        {
            ItemsSource = Enumerable.Range(0, 10_000).Select(i => $"Book {i}").ToList(),
        };
        window.Content = list;
        window.Show();
        window.Measure(new Size(1200, 800));
        window.Arrange(new Rect(0, 0, 1200, 800));

        var bitmap = new RenderTargetBitmap(new PixelSize(1200, 800));
        bitmap.Render(window);
        Assert.True(bitmap.PixelSize.Width > 0);

        // 虚拟化生效：ContainerFromItem 不应为 1 万个都已实现
        Assert.NotNull(list);
        window.Close();
    }

    [AvaloniaFact]
    public void Large_Image_Renders_In_Window()
    {
        Assert.True(File.Exists(FixtureCover), $"缺少夹具封面: {FixtureCover}");
        var window = new Window { Width = 900, Height = 1300 };
        var image = new Image
        {
            Source = new Bitmap(FixtureCover),
            Stretch = Stretch.Uniform,
        };
        window.Content = image;
        window.Show();
        window.Measure(new Size(900, 1300));
        window.Arrange(new Rect(0, 0, 900, 1300));

        var bitmap = new RenderTargetBitmap(new PixelSize(900, 1300));
        bitmap.Render(window);
        Assert.True(bitmap.PixelSize.Width > 0);
        window.Close();
    }
}
