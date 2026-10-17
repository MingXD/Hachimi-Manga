using Avalonia.Headless.XUnit;
using MangaAgent.App.ViewModels;
using MangaAgent.Core.Library;
using MangaAgent.Core.Storage;
using Xunit;

namespace MangaAgent.Tests.Library;

/// <summary>v4.3 文件夹自定义封面：相对路径校验 / 存储往返 / 条目显示与清除回退。</summary>
public class FolderCoverTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static async Task WaitUntilAsync(Func<bool> cond, int timeoutMs = 8000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!cond() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(30);
        Assert.True(cond(), "条件超时未满足");
    }

    [Fact]
    public void Validate_RelPath_Accepts_Inside_Rejects_Outside_And_NonImage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_cover_v_{Guid.NewGuid():N}");
        var sub = Path.Combine(root, "作者A");
        var outside = Path.Combine(Path.GetTempPath(), $"manga_cover_out_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(sub);
            Directory.CreateDirectory(outside);
            File.WriteAllBytes(Path.Combine(root, "a.png"), TinyPng);
            File.WriteAllBytes(Path.Combine(sub, "b.png"), TinyPng);
            File.WriteAllBytes(Path.Combine(root, "note.txt"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(outside, "evil.png"), TinyPng);

            Assert.True(ReadingStateStore.ValidateFolderCoverRelPath(root, "a.png", out _));
            Assert.True(ReadingStateStore.ValidateFolderCoverRelPath(root, "作者A/b.png", out _));
            Assert.True(ReadingStateStore.ValidateFolderCoverRelPath(root, "作者A\\b.png", out _));
            Assert.False(ReadingStateStore.ValidateFolderCoverRelPath(root, "../evil.png", out _));
            Assert.False(ReadingStateStore.ValidateFolderCoverRelPath(root, "note.txt", out _));
            Assert.False(ReadingStateStore.ValidateFolderCoverRelPath(root, "不存在.png", out _));
            // 绝对路径指向文件夹外 → 拒绝
            Assert.False(ReadingStateStore.ValidateFolderCoverRelPath(root, Path.Combine(outside, "evil.png"), out _));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
            try { Directory.Delete(outside, true); } catch { }
        }
    }

    [Fact]
    public void Store_Set_Load_Batch_Clear()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_cover_s_{Guid.NewGuid():N}");
        var sub = Path.Combine(root, "作者A");
        var db = Path.Combine(Path.GetTempPath(), $"manga_cover_db_{Guid.NewGuid():N}.db");
        try
        {
            Directory.CreateDirectory(sub);
            File.WriteAllBytes(Path.Combine(root, "a.png"), TinyPng);
            File.WriteAllBytes(Path.Combine(sub, "b.png"), TinyPng);
            using var store = new ReadingStateStore(db);
            Assert.True(store.SetFolderCover(root, "a.png"));
            Assert.True(store.SetFolderCover(sub, "b.png"));
            Assert.Equal("a.png", store.LoadFolderCover(root));
            var batch = store.LoadFolderCovers(new[] { root, sub, Path.Combine(root, "无") });
            Assert.Equal(2, batch.Count);
            Assert.Equal("a.png", batch[root]);
            Assert.Equal("b.png", batch[sub]);
            store.ClearFolderCover(root);
            Assert.Null(store.LoadFolderCover(root));
            // 非法相对路径不写入
            Assert.False(store.SetFolderCover(sub, "../a.png"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }

    [Fact]
    public void MoveFolderCover_Migrates_Record_To_New_Path()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_cover_mv_{Guid.NewGuid():N}");
        var db = Path.Combine(Path.GetTempPath(), $"manga_cover_mv_db_{Guid.NewGuid():N}.db");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, "a.png"), TinyPng);
            using var store = new ReadingStateStore(db);
            Assert.True(store.SetFolderCover(root, "a.png"));
            // 模拟目录移动：旧路径 → 新路径（相对路径不变）
            var movedRoot = root + "_moved";
            store.MoveFolderCover(root, movedRoot);
            Assert.Null(store.LoadFolderCover(root));                     // 旧键失效
            Assert.Equal("a.png", store.LoadFolderCover(movedRoot));      // 新键可用
            // 目标已有旧记录 → 被清理后迁移（目标须为真实存在的文件夹+图片）
            var xRoot = movedRoot + "_x";
            Directory.CreateDirectory(xRoot);
            File.WriteAllBytes(Path.Combine(xRoot, "b.png"), TinyPng);
            Assert.True(store.SetFolderCover(xRoot, "b.png"));
            store.MoveFolderCover(movedRoot, xRoot);
            Assert.Equal("a.png", store.LoadFolderCover(xRoot));
            // 源无记录 → 无操作
            store.MoveFolderCover(movedRoot + "_no", movedRoot + "_no2");
            Assert.Null(store.LoadFolderCover(movedRoot + "_no2"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task Entry_Shows_Cover_And_Clears_Back()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_cover_e_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, "cover.png"), TinyPng);
            var node = new FolderNode { Name = "作者A", Path = root };
            var entry = new FolderEntryItemViewModel(node) { FolderCoverW = 144, FolderCoverH = 155 };
            Assert.False(entry.HasCover);
            entry.SetFolderCover(root, "cover.png");
            await WaitUntilAsync(() => entry.HasCover);
            Assert.NotNull(entry.Thumbnail);
            entry.ClearFolderCover();
            Assert.False(entry.HasCover);
            Assert.Null(entry.Thumbnail);
            // 非法封面（文件夹外）不生效
            var evil = new FolderEntryItemViewModel(node);
            evil.SetFolderCover(root, "../evil.png");
            await Task.Delay(100);
            Assert.False(evil.HasCover);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
