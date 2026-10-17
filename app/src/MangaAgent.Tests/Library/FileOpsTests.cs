using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.Library;

/// <summary>文件操作回归（v3 #3）：跨卷回退复制删源、错误不再静默、源=目标/子目录等异常处理。</summary>
public class FileOpsTests
{
    [Fact]
    public void MoveTo_SameVolume_Directory_Succeeds()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_fops_{Guid.NewGuid():N}");
        try
        {
            var src = Path.Combine(root, "src");
            var dst = Path.Combine(root, "dst");
            Directory.CreateDirectory(Path.Combine(src, "sub"));
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            File.WriteAllText(Path.Combine(src, "sub", "b.txt"), "b");

            var (ok, err) = FileOps.MoveTo(src, dst);
            Assert.True(ok, err);
            Assert.False(Directory.Exists(src), "源目录应已移动");
            Assert.True(File.Exists(Path.Combine(dst, "src", "a.txt")));
            Assert.True(File.Exists(Path.Combine(dst, "src", "sub", "b.txt")));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void MoveTo_SameVolume_File_Succeeds()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_fops_{Guid.NewGuid():N}");
        try
        {
            var src = Path.Combine(root, "manga.cbz");
            var dst = Path.Combine(root, "dst");
            Directory.CreateDirectory(dst);
            File.WriteAllText(src, "data");
            var (ok, err) = FileOps.MoveTo(src, dst);
            Assert.True(ok, err);
            Assert.False(File.Exists(src));
            Assert.True(File.Exists(Path.Combine(dst, "manga.cbz")));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void MoveTo_SourceEqualsDest_ReturnsError()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_fops_{Guid.NewGuid():N}");
        try
        {
            var dir = Path.Combine(root, "book");
            Directory.CreateDirectory(dir);
            // 目标目录=源目录（同名不追加后缀时 UniquePath 会给 "book (2)"，这里用源自身的父目录模拟同名目标）
            var (ok, err) = FileOps.MoveTo(dir, root);
            Assert.False(ok);
            Assert.Contains("相同", err);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void MoveTo_DestInsideSource_ReturnsError()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_fops_{Guid.NewGuid():N}");
        try
        {
            var src = Path.Combine(root, "book");
            Directory.CreateDirectory(Path.Combine(src, "inner"));
            // 目标 = 源内部子目录 → 拒绝（防无限递归/移动自身）
            var (ok, err) = FileOps.MoveTo(src, Path.Combine(src, "inner"));
            Assert.False(ok);
            Assert.Contains("子目录", err);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void MoveTo_MissingSource_ReturnsError()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_fops_{Guid.NewGuid():N}");
        try
        {
            var (ok, err) = FileOps.MoveTo(Path.Combine(root, "nope"), root);
            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(err));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void CopyTo_ExistingName_GetsSuffix()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_fops_{Guid.NewGuid():N}");
        try
        {
            var src = Path.Combine(root, "src");
            var dst = Path.Combine(root, "dst");
            Directory.CreateDirectory(src);
            Directory.CreateDirectory(dst);
            File.WriteAllText(Path.Combine(src, "f.txt"), "a");
            // 目标已存在同名 → 追加后缀 (2)
            Directory.CreateDirectory(Path.Combine(dst, "src"));
            var (ok, err) = FileOps.CopyTo(src, dst);
            Assert.True(ok, err);
            Assert.True(Directory.Exists(Path.Combine(dst, "src (2)")));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void SameVolume_On_Same_Drive()
    {
        var a = Path.Combine(Path.GetTempPath(), "a.txt");
        var b = Path.Combine(Path.GetTempPath(), "sub", "b.txt");
        Assert.True(FileOps.SameVolume(a, b));
    }

    [Fact]
    public async Task CopyToAsync_Reports_Progress_And_Completes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_fops_{Guid.NewGuid():N}");
        try
        {
            var src = Path.Combine(root, "src");
            var dst = Path.Combine(root, "dst");
            Directory.CreateDirectory(Path.Combine(src, "sub"));
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            File.WriteAllText(Path.Combine(src, "b.txt"), "b");
            File.WriteAllText(Path.Combine(src, "sub", "c.txt"), "c");

            var reports = new List<(int Done, int Total)>();
            var progress = new Progress<(int Done, int Total)>(p => reports.Add(p));
            var (ok, err) = await FileOps.CopyToAsync(src, dst, progress);
            Assert.True(ok, err);
            Assert.Equal(3, reports.Count);                 // 3 个文件逐个报告
            Assert.Equal((3, 3), reports[^1]);              // 最后报告 = 完成
            Assert.True(File.Exists(Path.Combine(dst, "src", "sub", "c.txt")));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public async Task MoveToAsync_SameVolume_No_Progress_Needed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_fops_{Guid.NewGuid():N}");
        try
        {
            var src = Path.Combine(root, "src");
            var dst = Path.Combine(root, "dst");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            var (ok, err, _) = await FileOps.MoveToAsync(src, dst);   // 同卷 Move，无进度（v4.12 增加 TargetPath）
            Assert.True(ok, err);
            Assert.False(Directory.Exists(src));
            Assert.True(File.Exists(Path.Combine(dst, "src", "a.txt")));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
