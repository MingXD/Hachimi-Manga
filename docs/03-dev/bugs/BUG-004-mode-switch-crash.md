# BUG-004：双页模式点击单页按钮闪退（ObjectDisposedException）

- **状态**：已修复（2026-08-19，压力验证 4/4 零崩溃，26/26 测试通过，待用户实测确认）
- **优先级**：高（模式切换是常用操作，直接闪退）
- **现象**：双页浏览时点击模式切换按钮（单页/滚动），应用闪退

## 排查过程（先查原因）

### 1. 复现 + 精确堆栈
真实应用加全局异常记录（AppDomain + Dispatcher.UnhandledException → crash 日志），
--diag 自动执行模式切换序列（翻页制造缓存压力 + 快速连续切换 3 轮）：
```
System.ObjectDisposedException: Cannot access a disposed object.
Object name: 'Ref<Avalonia.Platform.IBitmapImpl>'.
   at Avalonia.Utilities.RefCountable.Ref`1.get_Item()
   at Avalonia.Media.Imaging.Bitmap.get_Size()
   at Avalonia.Controls.Image.MeasureOverride(Size availableSize)
```

### 2. 根因分析
- **位图生命周期管理缺陷**：页面位图由 VM 的 LRU 缓存（上限 6 页）统一管理，
  `EvictCache()` 淘汰和 `DecodeAsync` 高清重解码时直接 `bmp.Dispose()`。
- **缓存共享同一对象给 UI**：`LoadCurrentAsync`/Webtoon 的 `GetPageBitmapAsync`
  返回的是缓存中的**同一个 Bitmap 对象**，同时被 Image 控件的 Source 引用。
- **触发链**：双页 → 点击切换 → 进入 Webtoon → `UpdateWebtoonPages`（async void）
  逐页解码 → 触发 LRU 淘汰 → **Dispose 掉仍被 UI Image 引用的位图** →
  下一次布局 `Image.MeasureOverride → Bitmap.get_Size()` → ObjectDisposedException → 闪退。
- 首次修复后压力测试仍抓到 1 次崩溃（**剩余竞态窗口**）：`await GetPageBitmapAsync`
  恢复前另一轮解码淘汰并 Dispose 了该位图，恢复后 `img.Source = bmp` 引用已释放对象。

## 修复方案（位图 UI 引用计数）
核心：**被 UI 引用的位图延迟 Dispose，直到引用归零且不在缓存**。

1. **VM 增加引用计数**（ReaderViewModel.cs）：
   - `Dictionary<Bitmap,int> _uiRefs`：AcquireUiBitmap / ReleaseUiBitmap
   - `TryDispose(bmp)`：UI 引用中或仍在缓存 → 不 Dispose
   - `EvictCache` / 重解码路径全部改走 `TryDispose`
2. **Acquire 提前到 DecodeAsync 返回前**：位图从缓存取出即受保护（消除
   "await 后 Source 赋值前被淘汰" 竞态窗口）
3. **LoadCurrentAsync**：替换 Image1/Image2 前 Release 旧位图（替换瞬间不 Dispose）；
   竞态丢弃路径也 Release，避免泄漏
4. **View（ReaderWindow.axaml.cs）**：
   - `UpdateWebtoonPages`：换面板前 Release 所有子 Image 的位图引用；
     加 `_webtoonSeq` 序列保护（await 后若已切走则丢弃）
   - `ExitWebtoon`：离开 Webtoon 时 Release 面板位图引用
5. **DisposeAsync**：窗口关闭时 Dispose 全部（含 UI 引用中的）

## 验证
- 压力复现（翻页 8 页 + 快速模式切换 3 轮 + 稳定 Webtoon）：**4/4 运行零崩溃**
  （修复前必现 ObjectDisposedException）
- 回归测试新增：`ModeSwitchCrashTests.Rapid_ModeSwitch_DoesNot_Throw_ObjectDisposed`
  （headless 真实窗口 + 翻页缓存压力 + 连续模式切换）
- 全量测试：**26/26 通过**

## 教训
1. **共享对象的 Dispose 必须考虑所有持有者**：缓存返回同一 Bitmap 给 UI 时，
   缓存方不能单方面 Dispose——需要引用计数或拷贝。
2. **async void + await 期间资源可能被他人释放**：await 恢复后使用资源前
   必须确认其仍有效（序列号/版本检查）。
3. **全局异常记录是闪退排查的基础设施**：真实应用无 hook 时闪退无痕，
   加了 Dispatcher.UnhandledException 日志后一次复现即拿到完整堆栈。

## 残留事项
- [ ] 用户实测确认（双页 ↔ 单页/滚动反复切换不再闪退）
- [ ] 重新发布 exe（app\publish）
