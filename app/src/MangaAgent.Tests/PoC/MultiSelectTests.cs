using Avalonia.Headless.XUnit;
using MangaAgent.App.ViewModels;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>v4.3 右键多选：批量复制/移动/删除（VM 层批量 API 正确性；UI 分发在 MainWindow code-behind）。</summary>
public class MultiSelectTests
{
    private static string MakeDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"manga_multi_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    [AvaloniaFact]
    public async Task Copy_Move_Delete_Handle_Multiple_Paths()
    {
        var src = MakeDir();
        var dest = MakeDir();
        try
        {
            var a = Path.Combine(src, "a.txt");
            var b = Path.Combine(src, "b.txt");
            var c = Path.Combine(src, "c.txt");
            File.WriteAllText(a, "a");
            File.WriteAllText(b, "b");
            File.WriteAllText(c, "c");

            var vm = new MainWindowViewModel();
            // 批量复制 2 项
            int copied = await vm.CopyPathsToAsync(new[] { a, b }, dest);
            Assert.Equal(2, copied);
            Assert.True(File.Exists(Path.Combine(dest, "a.txt")));
            Assert.True(File.Exists(Path.Combine(dest, "b.txt")));
            Assert.True(File.Exists(a) && File.Exists(b));   // 复制保留源

            // 批量移动 1 项
            int moved = await vm.MovePathsToAsync(new[] { a }, dest);
            Assert.Equal(1, moved);
            Assert.False(File.Exists(a));
            Assert.True(File.Exists(Path.Combine(dest, "a.txt")));

            // 批量删除 2 项（回收站；无桌面环境时跳过断言）
            int deleted = vm.DeletePaths(new[] { b, c });
            if (deleted > 0)
            {
                Assert.False(File.Exists(b));
                Assert.False(File.Exists(c));
            }
        }
        finally
        {
            try { Directory.Delete(src, true); } catch { }
            try { Directory.Delete(dest, true); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task CopyPathsTo_Skips_Missing_Paths()
    {
        var src = MakeDir();
        var dest = MakeDir();
        try
        {
            var a = Path.Combine(src, "a.txt");
            File.WriteAllText(a, "a");
            var vm = new MainWindowViewModel();
            int copied = await vm.CopyPathsToAsync(new[] { a, Path.Combine(src, "不存在.txt") }, dest);
            Assert.Equal(1, copied);
            Assert.Equal("已复制 1 项", vm.StatusText);
        }
        finally
        {
            try { Directory.Delete(src, true); } catch { }
            try { Directory.Delete(dest, true); } catch { }
        }
    }
}
