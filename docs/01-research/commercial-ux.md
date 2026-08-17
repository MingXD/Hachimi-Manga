# 商业与移动端阅读器 UX 调研

> 调研范围：Windows 老牌阅读器（CDisplayEx / HoneyView / ComicRack）、移动端现代阅读器（Perfect Viewer / Chunky / Panels / Aidoku / Mihon-Tachiyomi 系）、桌面电子书与库管理（Foliate / Calibre）、网页漫画平台（Webtoon / ComicWalker），以及自托管阅读服务器（Kavita / Komga / YACReader）作为交叉参考。所有结论均标注来源链接。
> 说明：任务清单中的 "Cover（macOS/iOS）" 经核实为 **Windows（Microsoft Store，UWP）** 应用（开发者 French Fry，官网明确 "Read and manage your comic books on Windows"），Komga 官方 issue 也以它为例说明 RTL 双页对齐算法，这对我们"Windows 优先"的项目反而更有参考价值。

---

## 一、阅读体验模式清单

### 1. 阅读方向与双页（RTL / 双页 / 封面 / 中缝）

- **阅读方向切换（LTR / RTL / 竖排）**
  - Perfect Viewer 提供"右开或左开"阅读方式；Mihon/Tachiyomi 有 L2R / R2L / Vertical 三种分页阅读器；Chunky 提供独立的 "Right-to-left reading mode"；KOReader 有 per-file 的 "read from right to left" 设置（[Perfect Viewer 功能页](https://rookiestudio-perfectviewer.andro.io/zh)、[Mihon 阅读器组件](https://deepwiki.com/mihonapp/mihon/4.1-reader-components-and-viewers)、[Chunky App Store](https://apps.apple.com/us/app/chunky-comic-reader/id663567628)、[KOReader issue #4583](https://github.com/koreader/koreader/issues/4583)）。
  - ComicRack 把方向作为**元数据**而非纯设置：ComicInfo.xml 内置 `<Manga>Yes/No</Manga>` 字段，阅读器据此决定方向（[MobileRead Wiki: ComicRack](https://wiki.mobileread.com/wiki/ComicRack)）。
  - 值得注意：KOReader 用户反馈——"方向"与"缩放模式"两个选项会互相锁定（fit 非全页时方向变灰），这是经典反面教材：**方向与缩放必须是正交的独立选项**（[KOReader issue #10224](https://github.com/koreader/koreader/issues/10224)）。

- **双页模式（Two-up）与自动切换**
  - Perfect Viewer："版面配置：自动单页显示、自动双页显示、随屏幕方向自动切换"——按屏幕方向自动决定单/双页（[Perfect Viewer 功能页](https://rookiestudio-perfectviewer.andro.io/zh)）。
  - Panels 的 "Standard" 预设："横屏时自动设为双页"；ComicWalker 横屏一次看两页；ComicRack "Full screen reading mode with two page display"；Chunky "Single-page or two-up page view"（[Panels 阅读器预设](https://guides.panels.app/read-content/reader-configurations)、[ComicWalker 实测](https://check.unwire.hk/2014/03/24/comic_walker-2/software/)、[MobileRead Wiki: ComicRack](https://wiki.mobileread.com/wiki/ComicRack)、[Chunky App Store](https://apps.apple.com/us/app/chunky-comic-reader/id663567628)）。

- **封面单独处理**
  - Panels："我们始终保持封面为单页，从第 2 页开始分组"（[Panels 双页分组指南](https://guides.panels.app/read-content/double-page-grouping)）。
  - ComicInfo.xml 规范里首页标记为 `Page Type="FrontCover"`，成为阅读器"封面不参与双页"的元数据依据（[Comic book archive - Wikipedia](https://en.wikipedia.org/wiki/Comic_book_archive)）。

- **中缝（gutter）与跨页对齐算法**
  - **Cover（Windows）的规则最清晰**（Komga 官方 issue 引用为范例）：RTL 书的第一页**左对齐**单独显示；RTL 书的末页（单页）**右对齐**；LTR 反之。即"双页模式中，单页靠向阅读起点/终点对应的一侧"，保证翻页自然（[Komga issue #670](https://github.com/gotson/komga/issues/670)）。
  - Panels：自动把相邻两页按 2-3、4-5 分组，横图（landscape）始终保持单页；长按缩略图可执行 **"Group from this page on"** 手动重排分组起点（[Panels 双页分组指南](https://guides.panels.app/read-content/double-page-grouping)）。
  - Kavita 双页渲染区分 **Comic（页0 页1｜页2 页3）** 与 **Manga（页1 页0｜页3 页2）** 两种排列顺序，并有 `ReverseDoublePage` 处理 RTL（[Kavita PR #1333](https://github.com/Kareadita/Kavita/pull/1333)）。
  - Cover（Windows）用户反馈 gutter 间隔不可调（适应高度/宽度时两页间有间隔，其他模式无间隔）——**间隔应可独立调节**（[小众软件论坛 Cover 帖](https://meta.appinn.net/t/topic/49250)）。

- **横跨页（spread / 宽图）处理**
  - Mihon 的 **Dual Page Split**：检测到宽幅图时自动把一张横图拆成左右两个虚拟页，翻页时先看左半再看右半（[Mihon 阅读器组件](https://deepwiki.com/mihonapp/mihon/4.1-reader-components-and-viewers)）。
  - KOReader：fit 高度 + 缩放整页时，跨页图会先显示一半、再翻到另一半，且方向应遵循 RTL（[KOReader issue #4583](https://github.com/koreader/koreader/issues/4583)）。
  - Kavita：跨页图在双页模式下要"生成足够长的分页区域"避免 fit 失败（[Kavita PR #1333](https://github.com/Kareadita/Kavita/pull/1333)）。

### 2. 翻页与滚动

- **点击翻页 / 点按区域（tap zones）**
  - Mihon 把导航做成一等公民：**Edge / L 形 / 左右 / 禁用** 四种点击区预设 + 可水平/垂直反相；Webtoon 模式与分页模式各有独立导航设置（[Mihon 阅读器配置](https://deepwiki.com/mihonapp/mihon/4.3-reader-configuration-and-preferences)）。
  - Perfect Viewer："触控屏幕可设定 11 种动作"（点击/双击/长按/滑动等各自绑定翻页、菜单、缩放…）（[Perfect Viewer 功能页](https://rookiestudio-perfectviewer.andro.io/zh)）。

- **键盘与物理键**
  - CDisplayEx（Windows 经典版）：鼠标滚轮、左右方向键、PageUp/PageDown 翻页，且"每一个功能的快捷键都可以自定义"（[ZOL 百科：CDisplayEx 使用技巧](https://xiazai.zol.com.cn/baike/461876.shtml)）。
  - ComicRack："User definable Keyboard shortcuts"（[MobileRead Wiki: ComicRack](https://wiki.mobileread.com/wiki/ComicRack)）；Mihon 支持音量键翻页（[Mihon 阅读器配置](https://deepwiki.com/mihonapp/mihon/4.3-reader-configuration-and-preferences)）。

- **滑动翻页与手势**
  - Perfect Viewer 支持"多点触控缩放、滑动翻页"；ComicWalker "左右拖曳可转页面，下方亦可快速转页"（[Perfect Viewer 功能页](https://rookiestudio-perfectviewer.andro.io/zh)、[ComicWalker 实测](https://check.unwire.hk/2014/03/24/comic_walker-2/software/)）。

- **翻页动画类型**
  - Panels 提供三种页过渡：**paginated（分页）/ continuous（连续自由滚动）/ page curl（卷页动画）**，page curl 是"怀旧实体书手感"的卖点（[Panels 阅读器预设](https://guides.panels.app/read-content/reader-configurations)、[Panels 官网](https://www.panels.app/)）。
  - Foliate：`animated` 开关控制翻页动画，另有 **reduce-animation（减少动画）** 选项（[Foliate 设置文档](https://deepwiki.com/johnfactotum/foliate/5.4-preferences-and-settings)）。
  - Mihon 有独立的 `pageTransitions` 设置项（[Mihon 阅读器配置](https://deepwiki.com/mihonapp/mihon/4.3-reader-configuration-and-preferences)）。

- **连续滚动 / 垂直卷动（webtoon 模式）**
  - 这是当前生态的"第三阅读范式"：Mihon 有独立 **WebtoonViewer**（连续垂直滚动，点击翻页距离 = 屏幕高 3/4，滚动速度快时自动隐藏菜单，滚动到章节边界预加载上下章）（[Mihon 阅读器组件](https://deepwiki.com/mihonapp/mihon/4.1-reader-components-and-viewers)）。
  - Panels 为 manhwa 提供 **Vertical scroll** 预设；CDisplayEx（Android）Pro 提供"连续页面，横向和纵向"；YACReader 10 加入连续滚动；Kavita 有 webtoon 阅读器；Foliate 的 `scrolled` 模式即连续滚动版 EPUB（[Panels 阅读器预设](https://guides.panels.app/read-content/reader-configurations)、[CDisplayEx Play](https://play.google.com/store/apps/details?id=com.progdigy.cdisplay&hl=en&gl=us)、[YACReader 官网](https://www.yacreader.com/)、[Kavita PR #1333](https://github.com/Kareadita/Kavita/pull/1333)、[Foliate 设置文档](https://deepwiki.com/johnfactotum/foliate/5.4-preferences-and-settings)）。
  - Webtoon 平台把垂直滚动做成**手势-进度直接映射**——设计批评文章称这是全 App "最强设计时刻"：零学习成本、反馈即时、无竞争性符号干扰（[IXD@Pratt: WEBTOON 设计批评](https://ixd.prattsi.org/2026/02/design-critique-webtoon-ios-app/)）。

- **翻页响应细节**
  - CDisplayEx（Android）："Advanced page scaling options that allow you to pre-set the number of moves to reach the opposite edge of the page"——预先设定"几次翻动滚到页尾"，即分步滚动强度可调（[CDisplayEx Play](https://play.google.com/store/apps/details?id=com.progdigy.cdisplay&hl=en&gl=us)）。
  - Perfect Viewer："快取下一页及上一页影像"（双向预取，翻页零等待）（[Perfect Viewer 功能页](https://rookiestudio-perfectviewer.andro.io/zh)）。

### 3. 缩放与导航

- **适应方式（fit）**
  - Mihon 的 `Image Scale Type`：Fit Screen / Fit Width / Fit Height 等；Panels：**Fit（完整装入）/ Fill（铺满裁切）/ Automatic**；Cover（Windows）：适应页面宽度/高度（[Mihon 阅读器配置](https://deepwiki.com/mihonapp/mihon/4.3-reader-configuration-and-preferences)、[Panels 阅读器预设](https://guides.panels.app/read-content/reader-configurations)、[小众软件论坛 Cover 帖](https://meta.appinn.net/t/topic/49250)）。

- **手势缩放与平移**
  - Perfect Viewer：多点触控缩放 + **依页面原点卷动**（从页面原点开始平移）；ComicWalker 竖屏横屏都可 Multi-Touch 缩放（[Perfect Viewer 功能页](https://rookiestudio-perfectviewer.andro.io/zh)、[ComicWalker 实测](https://check.unwire.hk/2014/03/24/comic_walker-2/software/)）。
  - YACReader：新增捏合手势 + **Ctrl+滚轮**缩放（桌面端滚轮缩放的典范）（[YACReader 官网](https://www.yacreader.com/)）。
  - Panels：**One-finger zoom**——单指即可在页内平移缩放，专为手机优化（[Panels 官网](https://www.panels.app/)）。

- **放大镜（loupe）**
  - ComicRack 有 magnifier（放大镜）功能；YACReader 10.2 升级为**矩形/圆形可选（带圆环）、尺寸记忆、边缘缓动**（放大区域可推到页边，方便查看边缘内容）（[MobileRead Wiki: ComicRack](https://wiki.mobileread.com/wiki/ComicRack)、[YACReader 官网](https://www.yacreader.com/)）。
  - Perfect Viewer 有"对话框放大镜"（对白气泡专用放大）（[Perfect Viewer 功能页](https://rookiestudio-perfectviewer.andro.io/zh)）。

- **面板级导航（panel-by-panel / guided view）**
  - Panels View：基于在数百页漫画上训练的 ML 模型自动检测分格并逐格放大，专为小屏优化（仍标记为 experimental，入口在 Settings → Panels Labs）（[Panels 面板视图指南](https://guides.panels.app/read-content/panels-view)）。
  - YACReader（Android）1.10：**panel-by-panel 自动面板检测**，并可在"全页 / 自动裁边 / 逐格阅读"三档间切换（[YACReader 官网](https://www.yacreader.com/)）。
  - Mihon 社区也提交过 guided view PR（[mihon PR #3381](https://github.com/mihonapp/mihon/pull/3381)）——趋势：逐格阅读正在从 Comixology 专利走向开源标配。

- **缩放锁定**
  - Panels 的 **Lock zoom**：放大一页后底部出现"Lock Zoom"横幅，点击后该缩放级别在后续翻页中保持；再缩放即解除（[Panels 锁定缩放指南](https://guides.panels.app/read-content/lock-zoom)）。

- **图像后处理（裁边 / 增强）**
  - 自动裁白边：Perfect Viewer（自动切除白边）、Mihon（crop borders，Webtoon 版还有侧边留白 webtoonSidePadding）、Cover（自动检测裁切无内容白边）、YACReader（auto margin trimming）（[Perfect Viewer 功能页](https://rookiestudio-perfectviewer.andro.io/zh)、[Mihon 阅读器组件](https://deepwiki.com/mihonapp/mihon/4.1-reader-components-and-viewers)、[小众软件论坛 Cover 帖](https://meta.appinn.net/t/topic/49250)、[YACReader 官网](https://www.yacreader.com/)）。
  - 增强滤镜：Panels 分三类——filters（降噪 reduce noise、锐化 sharpness）、transformations（去边框 remove borders）、shaders（去网纹 reduce moire，考虑图幅与屏幕尺寸）（[Panels 图像滤镜指南](https://guides.panels.app/read-content/image-filters)）。
  - 老扫描件修复：Chunky 的 **auto contrast/tint** 自动修复泛黄页面与褪色墨水（[Chunky App Store](https://apps.apple.com/us/app/chunky-comic-reader/id663567628)）。
  - 缩放插值算法：Perfect Viewer（平均像素/双线性/Bicubic/Lanczos3 可选）、YACReader（Image adjustments 里可换 Lanczos）（[Perfect Viewer 功能页](https://rookiestudio-perfectviewer.andro.io/zh)、[YACReader 官网](https://www.yacreader.com/)）。

- **初始缩放焦点**
  - Mihon 的 `zoomStart`：打开页面时的初始焦点可设（如 R2L 漫画默认右上角），放大后先看到阅读起点（[Mihon 阅读器配置](https://deepwiki.com/mihonapp/mihon/4.3-reader-configuration-and-preferences)）。

### 4. 主题与护眼

- **明暗主题**
  - Foliate：主题选择器（跟随系统 / 亮 / 暗），阅读视图内直接切换，另有 `invert`（暗色下反色）与亮度倍率（[Foliate 设置文档](https://deepwiki.com/johnfactotum/foliate/5.4-preferences-and-settings)）。
  - YACReader 10：**12 套内置主题 + 主题编辑器 + 跟随系统**，且主题在 YACReader 与 YACReaderLibrary 统一生效（[YACReader 官网](https://www.yacreader.com/)）。
  - Panels 的背景色更激进：**Black / White / Dynamic / Immersive**——Dynamic 分析每页主色作背景，Immersive 在多页主色间渐变动画（[Panels 阅读器预设](https://guides.panels.app/read-content/reader-configurations)）。
  - 夜间模式：CDisplayEx（Android，Night mode）、Cover（Windows，夜间模式）均为一键开关（[CDisplayEx Play](https://play.google.com/store/apps/details?id=com.progdigy.cdisplay&hl=en&gl=us)、[小众软件论坛 Cover 帖](https://meta.appinn.net/t/topic/49250)）。

- **亮度与暗夜压暗**
  - Mihon：**自定义亮度 -75～100**，负值用半透明黑罩模拟"低于硬件下限"的暗（夜间被窝神器）；另有灰度、反色、RGB/Alpha 通道滤镜（[Mihon 阅读器配置](https://deepwiki.com/mihonapp/mihon/4.3-reader-configuration-and-preferences)）。
  - ComicWalker：阅读器 Menu 内直接调亮度（[ComicWalker 实测](https://check.unwire.hk/2014/03/24/comic_walker-2/software/)）。
  - Kavita 漫画阅读器内置"再压暗"（darkness level，"when nightlight just isn't dark enough"）（[Kavita PR #1333](https://github.com/Kareadita/Kavita/pull/1333)）。
  - Perfect Viewer：调整亮度、对比及 Gamma（[Perfect Viewer 功能页](https://rookiestudio-perfectviewer.andro.io/zh)）。

- **E-Ink 适配**：Mihon 提供 `flashOnPageChange` / `flashDurationMillis`（E-Ink 屏幕整页刷新的黑闪控制）——小众但精准的护眼功能（[Mihon 阅读器配置](https://deepwiki.com/mihonapp/mihon/4.3-reader-configuration-and-preferences)）。

### 5. 进度与书签

- **续读（resume）**
  - CDisplayEx（Windows 经典）：**Ctrl+R 恢复上次阅读**，是 20 年前就有的续读快捷键（[ZOL 百科：CDisplayEx 使用技巧](https://xiazai.zol.com.cn/baike/461876.shtml)）。
  - Panels：记录每个漫画的阅读会话（含阅读时长），驱动 **"Reading now"** 首页，进度跨 iPhone/iPad/Mac 同步，且**文件删除后重下仍保留进度**（按书身份而非文件路径存）——用户明确称赞这一点（[Panels 无痕模式指南](https://guides.panels.app/read-content/incognito-mode)、[Panels 官网](https://www.panels.app/)）。
  - Kavita / YACReader：服务端存进度，多设备续读；YACReader 修复过"读完当前本后下一本不自动标记为已开始"的问题（[Kavita 官网](https://www.kavitareader.com/)、[YACReader 官网](https://www.yacreader.com/)）。

- **书签**
  - Perfect Viewer：书签的新增/修改/删除；ComicWalker：Menu 内书签功能；CDisplayEx（Android）Pro 支持书签；Kavita 双页 Manga 模式修过"书签只记一页"的 bug（[Perfect Viewer 功能页](https://rookiestudio-perfectviewer.andro.io/zh)、[ComicWalker 实测](https://check.unwire.hk/2014/03/24/comic_walker-2/software/)、[CDisplayEx Play](https://play.google.com/store/apps/details?id=com.progdigy.cdisplay&hl=en&gl=us)、[Kavita PR #1333](https://github.com/Kareadita/Kavita/pull/1333)）。

- **阅读进度与已读/未读**
  - ComicRack：智能列表（Smart Lists）可做"从未读过"等动态过滤；Cover（Windows）：书架显示已读/未读状态、页数、当前页；Mihon：章节读完自动标记 + 库内"已读/未读/下载"筛选；Aidoku：库角标显示未读章节数（[MobileRead Wiki: ComicRack](https://wiki.mobileread.com/wiki/ComicRack)、[小众软件论坛 Cover 帖](https://meta.appinn.net/t/topic/49250)、[Mihon 阅读器配置](https://deepwiki.com/mihonapp/mihon/4.3-reader-configuration-and-preferences)、[Aidoku FAQ](https://aidoku.app/help/faq/)）。
  - 读完判定细节：Aidoku "必须翻到最后一页才标记已读"，并提供 **Incognito mode**（不产生已读/会话记录）、**重复章节一键标记已读**（多源库去重痛点）（[Aidoku FAQ](https://aidoku.app/help/faq/)、[Aidoku v0.6.11 更新解析](https://blog.gitcode.com/6bd2d87e2831a89d9010e93dde1a3d8e.html)）。

- **跨设备/跟踪同步**
  - Aidoku：iCloud 同步库 + AniList/MAL/Shikimori 进度跟踪；Panels：iCloud Drive / OPDS 双通道；Kavita：Kavita+ 可选同步 AniList/MAL；Chunky：云盘直读（[Aidoku GitHub](https://github.com/Aidoku/Aidoku)、[Panels 官网](https://www.panels.app/)、[Kavita 官网](https://www.kavitareader.com/)、[Chunky App Store](https://apps.apple.com/us/app/chunky-comic-reader/id663567628)）。

### 6. 每本书独立设置与默认值分层（横切维度）

- **Mihon 模型**：全局默认（defaultReadingMode 等）→ **每部漫画覆盖**（阅读模式、方向在阅读器中直接改并写入该漫画元数据），运行时按"有覆盖用覆盖，否则用全局"解析（[Mihon 阅读器配置](https://deepwiki.com/mihonapp/mihon/4.3-reader-configuration-and-preferences)）。
- **Panels 模型**：全局**预设（Preset）系统**（5 个默认预设可改名/删除，删光自动恢复）→ 阅读中 "..." → Configure reader 可临时调整当前书（不保存），或 **Save 为命名预设**（[Panels 阅读器预设](https://guides.panels.app/read-content/reader-configurations)）。
- **ComicRack 模型**：**Workspaces（工作区）** 保存整套界面/显示设置；每本书有自己的背景/纸张纹理等 Book Display Settings；方向等随 ComicInfo 元数据走（[ComicRackCE GitHub](https://github.com/maforget/ComicRackCE)、[MobileRead Wiki: ComicRack](https://wiki.mobileread.com/wiki/ComicRack)）。
- **KOReader 模型**：per-file 设置（方向、缩放记忆）随书保存（[KOReader issue #4583](https://github.com/koreader/koreader/issues/4583)）。
- **Kavita 模型**：库级设置（漫画/漫画书/书库类型决定默认阅读器行为）+ 用户级设置（主题、阅读偏好）（[Kavita 官网](https://www.kavitareader.com/)）。

---

## 二、代表性软件深度笔记

### CDisplayEx（Windows 经典 + Android）
- **血统**：从 CDisplay 开源而来，免费、绿色便携，用 7-Zip 插件直接读压缩包：*.CBR / *.CBZ / *.CBT / *.RAR / *.ZIP / *.TAR / *.7Z / *.LZH / *.ARJ / *.CAB / *.TAR.GZ / *.TAR.BZ2，图片 JPEG/PNG/GIF/BMP（[ZOL 百科：CDisplayEx](https://xiazai.zol.com.cn/baike/461876.shtml)）。
- **UI 哲学**：极简到"打开后一片空（全屏），一切操作都在右键菜单"；**每个功能快捷键均可自定义**——Ctrl+L 打开、Ctrl+R 恢复上次阅读、Ctrl+F 适合屏幕、Ctrl+D 单/双页切换、Ctrl+I/B 显示底部/侧边滚动条、Ctrl+W 全屏、Ctrl+T 缩略图、ESC 退出（[ZOL 百科：CDisplayEx](https://xiazai.zol.com.cn/baike/461876.shtml)）。
- **翻页**：鼠标滚轮、←/→、PageUp/PageDown；无动画、即时切换——"加载立即、阅读流畅"是它的招牌（[CDisplayEx Play](https://play.google.com/store/apps/details?id=com.progdigy.cdisplay&hl=en&gl=us)）。
- **Android 版（Pro 付费）扩展**：连续页面（横向/纵向）、"预设几次翻动滚到页尾"的分步滚动、书签、夜间模式、S-Pen、图片导出、Samba 网络直读（无需整体下载）、Komga/Kavita/OneDrive/Dropbox/Mega 接入；**库管理**：指定目录后"按系列分组，或提示你读系列里的下一册"（[CDisplayEx Play](https://play.google.com/store/apps/details?id=com.progdigy.cdisplay&hl=en&gl=us)）。
- **可借鉴**：快捷键全可自定义 + 右键菜单 = 桌面端"零学习成本 + 深度可定制"的原型；"下一册"推荐是系列阅读器独有的续读钩子。

### Perfect Viewer（Android）
- **定位**：速度极快的全能漫画/图片/电子书阅读器（[PCMag 评测](https://au.pcmag.com/mobile-phones/850/perfect-viewer-for-android)）。
- **格式**：ZIP/CBZ、RAR/CBR、7Z/CB7、LZH、CBT/TAR；EPUB/HTML/TEXT/青空文库（支持**中文直书排版**）；PDF 需插件；CIFS/SAMBA/FTP 直开；Google Drive/Dropbox/OneDrive 靠来源插件（[Perfect Viewer 功能页](https://rookiestudio-perfectviewer.andro.io/zh)）。
- **版面**：自动单页/自动双页/随方向自动切换；右开或左开；依页面原点卷动。
- **性能**：无图像尺寸上限、快取上一页/下一页、四种缩放插值（平均像素/双线性/Bicubic/Lanczos3）。
- **工具链**：书签 CRUD、幻灯片播放、书柜、11 种触控动作、对话框放大镜、自动切除白边、亮度/对比/Gamma、Chromecast、捐赠版"自动上色"（黑白转 4 色）（[Perfect Viewer 功能页](https://rookiestudio-perfectviewer.andro.io/zh)）。
- **可借鉴**：触控动作绑定表（11 种手势 × 动作）是移动端"全可定制"的标杆；自动裁边 + 缩放插值选择是"老扫描件"用户的核心需求。

### Cover（Windows · French Fry）★ 与我们最相关的竞品
- **一句话**：Windows UWP 设计、界面简洁美观的本地漫画阅读器；免费限 25 本，付费解锁无限（[小众软件论坛 Cover 帖](https://meta.appinn.net/t/topic/49250)、[French Fry 官网](https://frenchfrysoftware.com/)）。
- **格式**：CBZ/ZIP、CBR/RAR、7Z/CB7、CBT、PDF、EPUB（仅图片模式）。
- **库**：书架管理、已读/未读状态、页数、当前页；存储支持本地文件夹/网络位置/OneDrive/Google Drive/Dropbox。
- **阅读**：单/双页显示、适应宽度/高度、背景色、连续页面、夜间模式；**自动检测并裁切无内容白边**；双页模式下可把相邻两页**拼接为一页**（用户评价识别偏严格）。
- **RTL 双页对齐（业界范例）**：RTL 书首页左对齐、末页右对齐；LTR 反之——Komga 官方 issue 直接引用它的截图作为"正确行为"（[Komga issue #670](https://github.com/gotson/komga/issues/670)）。
- **被吐槽的点（我们的机会）**：库管理薄弱；读完不自动切下一本；双页间隔不可调；全屏时自动隐藏的鼠标在翻页时重新冒出（[小众软件论坛 Cover 帖](https://meta.appinn.net/t/topic/49250)）。
- **可借鉴**：美观优先 + 自动裁边 + 按阅读方向对齐双页；同时暴露了"功能完整度"与"美观"难以兼得的市场空缺——这正是我们的切入点。

### Panels（iOS / iPadOS / Mac）
- **定位**：DRM-free 漫画阅读器 + 官方无 DRM 商店；1.1M+ 下载、4.8 分（[Panels 官网](https://www.panels.app/)）。
- **预设系统**：5 个默认阅读预设（Standard 横屏自动双页 / Vertical scroll / Fill screen / Adjust to orientation / Page curl），可改名、删除，删光自动恢复（[Panels 阅读器预设](https://guides.panels.app/read-content/reader-configurations)）。
- **阅读器配置项**：页过渡（分页/连续/卷页）、滚动方向（横/竖）、页数（一/两/按屏宽自动）、页面适配（Fit/Fill/自动）、背景（黑/白/Dynamic 主色/Immersive 渐变主色）（[Panels 阅读器预设](https://guides.panels.app/read-content/reader-configurations)）。
- **双页分组**：封面恒单页、从第 2 页自动分组、横图恒单页；长按缩略图 "Group from this page on" 手动改分组（[Panels 双页分组指南](https://guides.panels.app/read-content/double-page-grouping)）。
- **AI 特性**：Panels View 逐格导航（ML 面板检测）；Live Text 文字/主体检测、选中翻译、角色贴纸（[Panels 面板视图指南](https://guides.panels.app/read-content/panels-view)、[Panels Live Text 指南](https://guides.panels.app/read-content/live-text)）。
- **其他**：Lock zoom（缩放跨页锁定）、图像滤镜三分类、Incognito mode、单指缩放、密码锁、OPDS 1.2（Komga/Kavita 流式直读）、iCloud Drive 同步、大库性能优化（[Panels 官网](https://www.panels.app/)、[Panels 锁定缩放指南](https://guides.panels.app/read-content/lock-zoom)）。
- **可借鉴**：预设体系 + 每书临时配置双轨；"Dynamic/Immersive 动态背景色"是差异化体验；把图像滤镜按 视觉/几何/屏幕相关 分三类很清晰。

### Chunky（iOS）
- **卖点**：极简 + 自组织。Smart upscaling（低清漫画智能放大）、Auto contrast/tint（自动修复泛黄/褪色）；"自组织库——把漫画丢进去就行"（从文件夹结构识别系列）（[Chunky App Store](https://apps.apple.com/us/app/chunky-comic-reader/id663567628)）。
- **阅读**：单页或 two-up、Right-to-left 阅读模式；CBZ/CBR/CBT/PDF；识别 ComicRack 与 ComicBookLover 标签。
- **导入生态**：iCloud Drive / Dropbox / Google Drive / OneDrive / Box / Transporter / 内置浏览器；Pro 解锁 SMB/FTP/SFTP/ComicStreamer/Calibre/Ubooquity + 自带 WebDAV 服务器（[Chunky App Store](https://apps.apple.com/us/app/chunky-comic-reader/id663567628)）。
- **家长控制**：标记"ok"的漫画 + 密码锁隐藏其余——隐私功能的产品化样板。
- **可借鉴**：把"修图级画质增强"（放大插值+自动色偏修复）直接前置为卖点；自组织库降低入门门槛。

### Aidoku（iOS/iPadOS）
- 免费开源、无广告、WASM 源插件系统、下载离线、AniList/MAL/Shikimori 跟踪（[Aidoku GitHub](https://github.com/Aidoku/Aidoku)）。
- 阅读器：方向锁定（横/竖）；翻到最后一页才标记已读；Incognito mode；重复章节一键标记已读；库角标未读章节数；iCloud 库同步（[Aidoku FAQ](https://aidoku.app/help/faq/)、[Aidoku v0.6.11 更新解析](https://blog.gitcode.com/6bd2d87e2831a89d9010e93dde1a3d8e.html)）。
- **可借鉴**："重复章节标记已读""读到最后一页才算读完"这类**多源库去重与读状态语义**，本地库同样需要（同名文件/重复卷）。

### ComicRack（Windows，精神续作 ComicRackCE）
- **阅读器**：多种显示模式、全屏、信息覆盖层（information overlays）、放大镜、色彩调整、自动背景色匹配、多标签页（同时开多本切换）、自动滚动、自动旋转、动态缩放、自动页面适配、manga mode、双屏支持、自定义快捷键（[MobileRead Wiki: ComicRack](https://wiki.mobileread.com/wiki/ComicRack)）。
- **管理器（业界标杆）**：库内分组/排序/堆叠/过滤；快速搜索；**Smart Lists（智能列表）** 动态过滤（从未读、按作者/系列…）；自定义列表；标签编辑；CSV 导出；库网络共享（带密码）（[MobileRead Wiki: ComicRack](https://wiki.mobileread.com/wiki/ComicRack)）。
- **元数据**：文件名智能解析（series/number/volume/year 自动填初值）；ComicInfo.xml 写入（Manga 标志、页类型 FrontCover 等）；批量转换 cbz/pdf/cbt；缩略图可定制（[MobileRead Wiki: ComicRack](https://wiki.mobileread.com/wiki/ComicRack)）。
- **ComicRackCE**：原版十年未更新后社区复活；新增智能列表字段、内置备份管理、插件生态（ComicVine/Amazon/MangaUpdate 刮削器、Library Organizer、Android 客户端）、**Workspaces**（含背景与纸张纹理设置）（[ComicRackCE GitHub](https://github.com/maforget/ComicRackCE)）。
- **可借鉴**：Smart Lists 是"把筛选固化成动态视图"的鼻祖；ComicInfo.xml 标准化是库互操作的关键（Calibre/Chunky/Kavita 都读它）。

### HoneyView（Windows）
- 轻量免费图像浏览器：**不解压直接浏览 ZIP/RAR/7Z/LZH/TAR/CBR/CBZ 内的图片**；动画 GIF/WebP；EXIF（含 GPS）；批量格式转换+缩放；社区评价"也针对漫画阅读做过优化"（[Bandisoft 官网](https://www.bandisoft.com/honeyview/)、[小众软件论坛](https://meta.appinn.net/t/topic/49250)）。
- 已停止更新，官方转向 BandiView 继承（[Bandisoft 官网](https://www.bandisoft.com/honeyview/)）。
- **可借鉴**："看压缩包当看图"的轻量路径——我们的文件管理可以直接支持 ZIP 内浏览而不必强制入库。

### Foliate（Linux 桌面 EPUB 阅读器）
- **视图设置**：scrolled（连续滚动）vs paginated（分页）；`animated` 翻页动画 + reduce-animation；主题（跟随/亮/暗）+ invert 反色 + 亮度倍率；`gap`（栏间距）、`max-column-count`（最多 10 栏）、`max-inline-size` / `max-block-size`（栏宽/页高，**支持竖排书写**的布局上限）（[Foliate 设置文档](https://deepwiki.com/johnfactotum/foliate/5.4-preferences-and-settings)）。
- **设置界面**：四页签设置窗口（字体 / 版面 / 颜色 / 行为）+ 阅读视图内 View 菜单快速访问 + 主题选择器圆钮（follow/light/dark）（[Foliate 设置文档](https://deepwiki.com/johnfactotum/foliate/5.4-preferences-and-settings)）。
- **可借鉴**：分栏/栏宽/页高是"竖排漫画/直书"的布局参数雏形；把 90% 常用设置放进阅读视图内、设置窗口只放剩余项，是桌面阅读器的标准分层。

### Calibre（库管理 UX 标杆）
- **三种库视图**：表格（可调列）、封面网格、3D 书架；**切换视图时保留选中项与滚动位置**（PreserveViewState）（[Calibre 库视图文档](https://deepwiki.com/kovidgoyal/calibre/4.2-library-views-and-display-modes)）。
- **Tag Browser**：作者/系列/标签/出版社/语言/格式/标识符/已存搜索/**用户分类（@ 前缀，点号嵌套）** 的分层树，点击即过滤（[Calibre 库视图文档](https://deepwiki.com/kovidgoyal/calibre/4.2-library-views-and-display-modes)）。
- **搜索语法**：全文关键词、字段:值、短语、布尔 AND/OR/NOT、比较、正则、集合成员（[Calibre 库视图文档](https://deepwiki.com/kovidgoyal/calibre/4.2-library-views-and-display-modes)）。
- **虚拟书库（Virtual Libraries）**：命名搜索 = 一个持久过滤器，影响所有视图、标签计数、搜索范围与批量操作（[Calibre 虚拟书库文档](https://manual.calibre-ebook.com/virtual_libraries.html)）。
- **性能工程**：懒加载 + 封面缩略图缓存 + 防抖计数，支撑万级书库（[Calibre 库视图文档](https://deepwiki.com/kovidgoyal/calibre/4.2-library-views-and-display-modes)）。
- **可借鉴**：虚拟书库 ≈ ComicRack Smart Lists 的姊妹概念；"列显隐/排序/对齐/字体自定义 + 一键恢复默认"是表格视图的完整自由度模板。

### 网页平台阅读模式：Webtoon 与 ComicWalker
- **Webtoon**：垂直滚动原生格式（縦読み），"手势-进度直接映射、反馈即时、无竞争性符号"被设计批评文章评为全 App 最佳设计；反面案例是发现页塞满排行榜/活动/货币的"过度符号竞争"，以及章节末突兀的付费墙（广告或金币解锁）打断叙事沉浸——**付费决策必须在沉浸前预告**（[IXD@Pratt: WEBTOON 设计批评](https://ixd.prattsi.org/2026/02/design-critique-webtoon-ios-app/)）；垂直滚动画布还催生了"把传统横版漫画重排版为竖滚"的产业（[ComicsBeat 专访](https://www.comicsbeat.com/interview-ethan-leblanc-on-reformatting-beneath-the-trees-where-nobody-sees-other-comics-to-webtoons-vertical-scroll/)）。
- **ComicWalker**（角川）：左右拖曳翻页 + 底部快速转页条；横屏双页；多指缩放；Menu 内含书签、**旋转锁定**（躺读）、亮度调节；杂志分类聚合连载；新刊更新通知；PC 网页版同样支持双页（[ComicWalker 实测](https://check.unwire.hk/2014/03/24/comic_walker-2/software/)）。
- **可借鉴**：旋转锁定与亮度是"躺读场景"的高频小功能；快速跳页条（页码滑杆）在长话/长单行本里比翻页更高效；垂直滚动与分页应作为两种并列模式而非互斥。

### 补充交叉参考：Mihon / YACReader / Kavita / KOReader（已在清单中引用，此处仅列要点）
- Mihon 的 Webtoon 点击距离（3/4 屏）、章节过渡页、导航预设、Dual Page Split、每书覆盖——现代移动阅读器的完整参考（[Mihon 阅读器组件](https://deepwiki.com/mihonapp/mihon/4.1-reader-components-and-viewers)、[Mihon 阅读器配置](https://deepwiki.com/mihonapp/mihon/4.3-reader-configuration-and-preferences)）。
- YACReader 的放大镜/主题编辑器/面板级阅读/实时翻译（[YACReader 官网](https://www.yacreader.com/)）。
- Kavita 的双页 Manga 顺序、darkness level、连续阅读（[Kavita PR #1333](https://github.com/Kareadita/Kavita/pull/1333)、[Kavita 官网](https://www.kavitareader.com/)）。
- KOReader 的 per-file 方向 + 缩放正交性教训（[KOReader issue #10224](https://github.com/koreader/koreader/issues/10224)）。

---

## 三、本地库与文件管理 UX 模式

1. **文件夹视图 ↔ 系列库视图双模式**
   - 低门槛：CDisplayEx "直接浏览文件夹找漫画"，Chunky "把漫画丢进去自动组织"（自组织库，按文件夹识别系列），Panels 把任意文件夹/iCloud 目录直接引用为库（[CDisplayEx Play](https://play.google.com/store/apps/details?id=com.progdigy.cdisplay&hl=en&gl=us)、[Chunky App Store](https://apps.apple.com/us/app/chunky-comic-reader/id663567628)、[Panels 官网](https://www.panels.app/)）。
   - 强管理：ComicRack 库（智能列表/分组/堆叠/过滤）、Calibre 库（标签浏览器/虚拟书库）、Kavita/Komga 系列+收藏+阅读列表（[MobileRead Wiki: ComicRack](https://wiki.mobileread.com/wiki/ComicRack)、[Calibre 库视图文档](https://deepwiki.com/kovidgoyal/calibre/4.2-library-views-and-display-modes)、[Kavita 官网](https://www.kavitareader.com/)）。
   - **结论**：两者不是二选一，而是同一批数据的两种投影，且切换时必须保留选中与滚动位置（Calibre 的 PreserveViewState）。

2. **扫描与智能识别**
   - ComicRack 文件名智能解析（series/number/volume/year）；Kavita 解析文件名 + ComicInfo.xml + 文件夹监视自动导入（[MobileRead Wiki: ComicRack](https://wiki.mobileread.com/wiki/ComicRack)、[Kavita 官网](https://www.kavitareader.com/)）。
   - 元数据标准化：ComicInfo.xml 是事实标准（ComicRack 写、Chunky/Calibre/Kavita 读、Calibre 有嵌入 CBZ 的插件）（[EmbedComicMetadata 插件](https://github.com/RichardScottOZ/EmbedComicMetadata)、[Calibre 写 CBZ 元数据](https://github.com/guilhermesimoes/guilhermesimoes.github.io/blob/master/_posts/2025-10-25-writing-metadata-to-cbz-with-calibre.md)）。

3. **封面墙与排序筛选**
   - 封面导向：Calibre 封面网格、ComicRack 缩略图网格、Panels "covers-first" 设计、Kavita 封面墙、Cover 书架（[Panels 官网](https://www.panels.app/)）。
   - 排序/筛选维度全集：状态（已读/未读/进行中）、更新时间、系列、卷期、作者、标签、评分；Mihon 库分类页签 + 状态角标；Kavita 全文搜索 + 智能筛选（[Mihon 阅读器配置](https://deepwiki.com/mihonapp/mihon/4.3-reader-configuration-and-preferences)、[Kavita 官网](https://www.kavitareader.com/)）。

4. **集合/标签/列表**
   - 静态集合：Calibre 用户分类（@ 嵌套）、Kavita/Komga Collections、Mihon 分类页签；
   - 动态集合：ComicRack Smart Lists、Calibre 虚拟书库、Kavita Smart Filters（[Calibre 库视图文档](https://deepwiki.com/kovidgoyal/calibre/4.2-library-views-and-display-modes)、[ComicRackCE GitHub](https://github.com/maforget/ComicRackCE)）。

5. **文件管理动作**
   - 库内重命名/移动/删除（Perfect Viewer 简易文件管理、ComicRack 批量转换+重命名、ComicRackCE Library Organizer 插件）；读后"下一册"推荐（CDisplayEx）；读完自动切下一本（Cover 缺失、用户呼声高）（[Perfect Viewer 功能页](https://rookiestudio-perfectviewer.andro.io/zh)、[小众软件论坛 Cover 帖](https://meta.appinn.net/t/topic/49250)）。

6. **进度与库的打通**
   - "Reading now / 继续阅读"入口：Panels（会话+时长）、Kavita（进度条）、Webtoon（顶部继续阅读横幅）；库封面角标显示进度百分比或未读角标（Aidoku/Mihon）（[Panels 无痕模式指南](https://guides.panels.app/read-content/incognito-mode)、[Aidoku FAQ](https://aidoku.app/help/faq/)）。

---

## 四、对我们的 UX 设计建议（高度可定制 + 默认值智能）

### 1. 设置分层：四层模型（全局 → 库/系列 → 单本 → 会话）
- **全局默认**（阅读方向、双页策略、主题、翻页方式、缩放插值、动画）——首次启动引导设一次即可。
- **库/系列级默认**（可选）：同一系列的书通常同方向同阅读习惯（参考 Kavita 库级设置、ComicRack 按系列元数据）。
- **单本覆盖**：阅读器中改动的方向/双页/主题写入该书（参考 Mihon per-manga overrides、KOReader per-file），且**进度/覆盖以书身份（内容哈希/ID）存储，不随文件路径漂移**（Panels 用户称赞"删了重下进度还在"）。
- **会话临时调整**：阅读器内快捷面板的调整只对当前会话生效，不污染设置（参考 Panels "Configure reader 不保存"）。
- **预设系统**：把整套阅读配置可命名保存/切换（Panels 5 预设、ComicRack Workspaces），预设可导入导出；用户不该在"方向+双页+主题+翻页"四个设置之间反复横跳。

### 2. 智能默认值：开局即对的体验
- **阅读方向**：优先级 = ComicInfo.xml 的 `<Manga>` 标志 → 文件名/目录启发式（manga/manhwa/日文名/作者）→ 兜底 RTL（核心用户是日漫读者）；任何时候可一键反相，且**方向与缩放/适配选项完全正交**（避免 KOReader #10224 的灰置 bug）。
- **双页策略**：横屏/宽窗口默认双页、竖屏默认单页（Panels/Perfect Viewer 的 automatic）；封面恒单页；宽高比 > 1.3 的横跨页识别为 spread 单独整页展示（Kavita/Mihon 的 spread 处理）。
- **中缝（gutter）**：默认"无缝拼接"（0 间隔，贴合纸书与日漫习惯）；提供 0/小/大 三档可调（Cover 用户抱怨不可调的教训）；**按阅读方向自动对齐**：RTL 首页左对齐、末页右对齐，LTR 反之（Cover 在 Komga issue 中的范式行为）。
- **主题**：默认跟随系统，首次打开给一次轻量三选（亮/暗/跟随）；夜间模式 = 暗主题 + 亮度软件压暗（Mihon -75~100 半透明黑罩方案），并提供"E-Ink/深色护眼"小开关。
- **翻页输入**：Windows 默认 = 键盘（←/→、PageUp/PageDown、空格）+ 滚轮 + 可自定义全部快捷键（CDisplayEx 传统）；未来移动端再叠加点击区预设（Edge/L/R/Kindle 式 + 反相，Mihon 方案）与滑动。

### 3. 阅读器内的一级快捷面板
高频项（方向、单/双页、主题/背景、缩放 fit、翻页方式）必须**在阅读中两步内可达**（Panels 的 "..." → Configure reader；Foliate 的 View 菜单；ComicWalker 的 Menu 亮度/书签/旋转锁），不要藏在设置深处；面板内所有改动即时生效（Mihon 的响应式偏好绑定）。

### 4. 三种阅读范式并列
**RTL 分页 / LTR 分页 / 连续垂直滚动（webtoon）** 三个模式平级存在，默认按内容推断（日漫→RTL 分页，韩漫/长条→垂直滚动）；垂直滚动同样提供"点击 3/4 屏翻一屏"的可选导航（Mihon WebtoonViewer）。

### 5. 图像质量是差异化卖点
- 自动裁白边（默认开，逐本可关）；缩放插值可选（Lanczos3 默认，YACReader/Perfect Viewer 同款）；去网纹/降噪/锐化滤镜（Panels 三分类）；泛黄自动修复（Chunky auto contrast/tint）。
- "打开即清晰"（Chunky 的 Smart upscaling）比任何宣传文案都有效。

### 6. 进度体系
- 续读是刚需：打开即回到上次位置，库里有"继续阅读"聚合入口；读完自动标记 + 手动标记并重；每本书书签；封面角标显示进度（百分比或未读角标，Aidoku/Mihon）。
- 系列流：读完一本推荐"下一册"（CDisplayEx 的 next-album）；提供"读完后自动打开下一本"开关（Cover 的公开槽点）。

### 7. 库：双视图 + 智能扫描 + 动态筛选
- 文件夹视图与系列视图可切换且保留上下文（Calibre PreserveViewState）；扫描时文件名智能解析（系列/卷/期）；支持 ComicInfo.xml 读写（生态互操作）。
- 筛选做成**可保存的动态视图**（ComicRack Smart Lists / Calibre 虚拟书库的思想）：如"未读 & 系列：XXX & 最近更新"；标签与集合静态组织并行。
- 大库性能基线：懒加载 + 封面缩略图缓存 + 防抖计数（Calibre 同款工程手段）。

### 8. 设置界面的组织
- 分组页签 + 全局搜索（Calibre 搜索语法 / YACReader 搜索指南）；每类设置给默认值与"恢复默认"；所有枚举型设置用芯片组（Mihon SettingsChipRow）而非下拉，减少点击层级。
- 高级项（图像滤镜、亮度曲线、动画、E-Ink）进设置页；高频项留在阅读器面板——**设置入口不超过两层**。

### 9. 面向未来的可移植性
- 阅读引擎与"默认值推断逻辑"与 UI 解耦：同一套设置模型未来直接映射到触控/点击区与手势（移动端）；预留 OPDS 客户端能力（Panels/Kavita 模式）与网络存储接入（Samba/SFTP/WebDAV，CDisplayEx/Chunky 已证明是 Pro 卖点）。

---

### 主要来源索引
- [CDisplayEx – Google Play](https://play.google.com/store/apps/details?id=com.progdigy.cdisplay&hl=en&gl=us) ｜ [CDisplayEx 使用技巧 – ZOL](https://xiazai.zol.com.cn/baike/461876.shtml)
- [Perfect Viewer 功能 – andro.io](https://rookiestudio-perfectviewer.andro.io/zh) ｜ [Perfect Viewer – PCMag](https://au.pcmag.com/mobile-phones/850/perfect-viewer-for-android)
- [HoneyView – Bandisoft](https://www.bandisoft.com/honeyview/)
- [ComicRack – MobileRead Wiki](https://wiki.mobileread.com/wiki/ComicRack) ｜ [ComicRackCE – GitHub](https://github.com/maforget/ComicRackCE)
- [Cover – French Fry](https://frenchfrysoftware.com/) ｜ [Cover 推荐帖 – 小众软件](https://meta.appinn.net/t/topic/49250)
- [Panels 官网](https://www.panels.app/) ｜ [Panels Guides（预设/双页分组/面板视图/滤镜/锁定缩放/Live Text/无痕）](https://guides.panels.app/read-content/reader-configurations)
- [Chunky – App Store](https://apps.apple.com/us/app/chunky-comic-reader/id663567628)
- [Aidoku – GitHub](https://github.com/Aidoku/Aidoku) ｜ [Aidoku FAQ](https://aidoku.app/help/faq/) ｜ [Aidoku v0.6.11 更新解析](https://blog.gitcode.com/6bd2d87e2831a89d9010e93dde1a3d8e.html)
- [Foliate 设置 – DeepWiki](https://deepwiki.com/johnfactotum/foliate/5.4-preferences-and-settings)
- [Calibre 库视图 – DeepWiki](https://deepwiki.com/kovidgoyal/calibre/4.2-library-views-and-display-modes) ｜ [Calibre 虚拟书库](https://manual.calibre-ebook.com/virtual_libraries.html)
- [WEBTOON 设计批评 – IXD@Pratt](https://ixd.prattsi.org/2026/02/design-critique-webtoon-ios-app/) ｜ [ComicWalker 实测 – unwire.hk](https://check.unwire.hk/2014/03/24/comic_walker-2/software/)
- [Mihon 阅读器组件/配置 – DeepWiki](https://deepwiki.com/mihonapp/mihon/4.1-reader-components-and-viewers)
- [Kavita 官网](https://www.kavitareader.com/) ｜ [Kavita 双页渲染 PR #1333](https://github.com/Kareadita/Kavita/pull/1333)
- [Komga issue #670（Cover RTL 对齐）](https://github.com/gotson/komga/issues/670) ｜ [KOReader issue #4583](https://github.com/koreader/koreader/issues/4583) ｜ [KOReader issue #10224](https://github.com/koreader/koreader/issues/10224)
- [YACReader 官网](https://www.yacreader.com/)
