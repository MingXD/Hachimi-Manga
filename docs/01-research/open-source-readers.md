# 开源漫画阅读器深度调研

> 调研范围：Android 阅读器（Mihon/Tachiyomi 生态、Kotatsu）、桌面阅读器（YACReader、OpenComic、Houdoku、MComix、MangaMeeya、Komikku/GNOME）以及自托管漫画服务器（Kavita、Komga、Calibre-Web）。数据截至调研时点（2026-08），均来自各项目官方仓库 README、官网文档、GitHub/GitLab/Codeberg API 与源码。本文作为竞品调研存档。

## 项目总览对比表

| 项目 | 定位/平台 | 技术栈 | 主要格式 | 许可证 | 活跃度（stars/最新版本/最后推送） | 阅读模式亮点 |
|---|---|---|---|---|---|---|
| **Mihon**（Tachiyomi 继任者） | Android 8.0+ 漫画阅读器 | Kotlin、Jetpack Compose + Material3、Coil3、OkHttp、SQLDelight、libarchive、WebGPU 实验渲染器 | ZIP/CBZ、RAR/CBR、EPUB（Local source）、图片文件夹 | Apache-2.0 | 22.9k stars；v0.20.4（2026-08-05）；非常活跃 | 默认 RTL；LTR/RTL/Vertical/Webtoon/Continuous 五种模式；双页视图（Never/Always/Wide）+ 宽页拆分/旋转；WebGPU 3D 翻页动画；E-ink 闪烁适配 |
| **Kotatsu**（已停运） | Android 6.0+ 漫画阅读器（在线源为主） | Kotlin、传统 View（RecyclerView）、Coil3、Room、Hilt、独立 kotatsu-parsers 仓库 | CBZ（第三方本地档案）；下载离线阅读 | GPL-3.0 | 8.8k stars；仓库已 archived（2025-11 停运公告） | 标准 + Webtoon 双阅读器；手势操作；Material You；平板/桌面窗口适配 |
| **Kotatsu-Redo**（接续 fork） | Android 漫画阅读器 | 同 Kotatsu | 同 Kotatsu | GPL-3.0 | 810 stars；2026-08 仍活跃 | 延续 Kotatsu 阅读器 |
| **YACReader** | Windows/macOS/Linux 桌面阅读器+库；iOS App（闭源）；Android 远程端 | C++20 + Qt6（QRhi 渲染）、CMake、7-Zip 后端 | RAR/ZIP/CBR/CBZ/TAR/PDF/EPUB（图片型，实验）；JPEG/GIF/PNG/TIFF/WebP/BMP | GPL-3.0 | 1.35k stars；10.2.0（2026-08-08）；活跃（单人主导） | 双页模式；矩形/圆形放大镜；连续滚动；Lanczos 插值；12 套内置主题+自定义主题；go-to-flow 页面流浏览 |
| **OpenComic** | Windows/macOS/Linux（Electron） | Node.js + Electron 43、TypeScript、jQuery 模板 UI、sharp/libvips、node-7z、pdfjs-dist、epubjs/foliate-js、onnxruntime（AI 超分/去网点） | 图片 JPG/JP2/JXR/JXL/PNG/APNG/AVIF/WEBP/GIF/SVG/BMP/ICO；压缩 RAR/ZIP/7Z/TAR/LZH/ACE/CBR/CBZ/CBA/CB7/CBT；PDF/EPUB(α)；SMB/FTP/SFTP/S3/WebDAV；OPDS | GPL-3.0 | 1.9k stars；v1.6.5（2025-10-31）；活跃 | Manga（RTL）/Webtoon 模式；双页视图；浮动放大镜；滤镜（亮度/饱和度/对比度/棕褐/反相）+ 黑白上色；多种缩放插值算法 |
| **Houdoku** | Windows/macOS/Linux 桌面 | Electron + React + Recoil、Radix/shadcn UI、Tiyo 插件体系 | 本地 ZIP 或图片文件夹导入；在线源经插件 | MIT | 1.1k stars；v2.16.0（2024-12-17）；开发放缓 | 单页/双页/长条三种布局；双页间距+offset（模拟实体书）；LTR/RTL 方向（联动快捷键）；图像尺寸限制 |
| **MComix**（Comix 继任） | Linux/BSD/Windows（Python/GTK） | Python + GTK3（PyGObject/cairo）、Pillow、poppler（PDF）、7-Zip 后端 | CBR/CBZ/CB7(7z)/CBT(tar)/LHA/PDF；ZIP/RAR/7Z/LHA 直读 | GPL-2.0 | 3.1.1（2025-09-06，SourceForge 仍在更新） | 双页模式；fit 宽/高/两者；智能滚动；Manga 模式（RTL）；书签+库；MComix3 分支加入 Lanczos 重采样 |
| **MangaMeeya**（经典） | Windows 仅 | 闭源免费软件（C++），已停止更新 | RAR/ZIP 直读（不支持 7Z）、图片文件夹 | 专有（曾违反 GPL） | 最终版 7.4（约 2011）；中文社区长期流传汉化版 | 预读取/缓存快；强大快捷键；书架管理；双页等视图 |
| **Komikku** | GNOME（Linux，Flatpak） | Python + GTK3 + libhandy、Meson、beautifulsoup4/lxml | 在线源下载 + 离线阅读 | GPL-3.0+ | Codeberg 106 stars；1.84.0（2025）；活跃 | RTL/LTR/Vertical/Webtoon 四种模式；键盘/鼠标/滚轮/双指/触屏多输入导航；亮暗主题 |
| **Kavita** | 自托管服务器（Win/Linux/macOS/Docker/NAS），响应式 Web 阅读器 | C#/.NET + Angular（Kavita-webui）、NetVips | CBR/CBZ/ZIP/RAR/RAR5/7z/原始图片；EPUB/PDF；库类型：Manga/Comic(Flexible)/Comic/EPUB/PDF/Images | GPL-3.0 | 11.5k stars；v0.9.0.2（2026-05-14，仍标 beta）；非常活跃 | Webtoon 连续模式；连续阅读（不离开阅读器）；双页布局+offset；LTR/RTL；阅读档案；主题系统；EPUB 虚拟页/批注 |
| **Komga** | 自托管媒体服务器（跨平台 JVM），响应式 Web 阅读器 | Kotlin + Spring Boot（后端）+ Vue3/TypeScript（Web UI） | 存档 ZIP/CBZ、RAR/CBR、7Z；PDF/EPUB；图片 JPEG/PNG/GIF/WebP（全平台）、JXL/AVIF/HEIF（原生平台） | MIT | 6.6k stars；1.26.3（2026-08-12）；非常活跃 | DIVINA webreader：LTR/RTL/Vertical/Webtoon；从元数据自动推断阅读方向；双页（首尾页/横向页单页）；按需转码（如 WEBP→JPEG） |
| **Calibre-Web** | 自托管 Web（电子书库管理，漫画可存储/OPDS 传输） | Python Flask + Jinja2 + Bootstrap3、ImageMagick/Ghostscript（封面） | 依托 Calibre 库（EPUB/PDF/MOBI/CBZ 等按 Calibre 支持）；浏览器内读 EPUB/PDF/TXT | GPL-3.0 | 17.9k stars；0.6.27（2026-08-08）；非常活跃 | 非漫画向阅读器；价值在元数据管理、权限、OPDS、Kobo 同步 |

## 逐项目深度解析
### 1. Mihon（Tachiyomi 继任 fork）与 Tachiyomi 生态

**背景**：Tachiyomi 是 Android 上最知名的开源漫画阅读器，2024 年 1 月在 Kakao Entertainment 的法律威胁下宣布停止开发并归档仓库（[Alternativeto 报道](https://alternativeto.net/news/2024/1/manga-reader-app-tachiyomi-ceases-development-amid-legal-threats-from-kakao-entertainment/)）。社区随后 fork 出 **Mihon**（[github.com/mihonapp/mihon](https://github.com/mihonapp/mihon)）作为事实上的继任者，由新团队（mihonapp 组织）接管开发；扩展（extensions）改由社区仓库 **Keiyoushi** 维护（Komga 官方扩展也托管于此，见后文）。

**技术栈**（依据 [libs.versions.toml](https://github.com/mihonapp/mihon/blob/main/gradle/libs.versions.toml) 与源码）：
- 语言/UI：Kotlin 2.4 + **Jetpack Compose**（Compose BOM 2026.07）+ Material3，导航用 Voyager，DI 用 Metro（Zac Sweers）；i18n 用 moko-resources。
- 图片：**Coil 3**（GIF 支持、OkHttp 网络层、Brotli/Zstd 压缩），磁盘缓存 DiskLruCache；缩放用自维护的 subsampling-scale-image-view fork 与 PhotoView；另有自研 ca.mpreg:imagedecoder。
- 数据：**SQLDelight**（SQLite，androidx sqlite bundled）+ Paging3；下载/网络用 OkHttp 5.5。
- 压缩包：**libarchive**（me.zhanghai.android.libarchive）——同一套库同时解 ZIP/CBZ、RAR/CBR、7z 等。
- 渲染亮点：**WebGPU 高质量渲染器（实验性）**——ca.mpreg:webgpuviewer，在开启“高质量渲染器”后 LTR/RTL/Vertical/Webtoon/Continuous 全部走 WebGPU 管线，支持 3D 翻页动画（Flip/Stack/Sphere/Cube/Fade 等 11 种）。
- 文件访问：unifile（SAF 封装）、Shizuku（Root/ADB 权限），解析用 jsoup、QuickJS。

**支持的格式**：Local source（本地阅读）支持“图片文件夹、ZIP/CBZ、RAR/CBR、EPUB”，官方文档明确“目录与 ZIP/CBZ 性能更好”；EPUB 整包被当作**单章**处理，不会自动拆章（[Local source 文档](https://mihon.app/docs/guides/local-source/)）。需要固定的目录约定：`[存储位置]/local/[系列名]/[章节名]/图片...`，压缩包内子文件夹结构被忽略，压缩包文件名即章节名；文档建议在 local 目录放 `.nomedia` 避免图片进入相册。

**阅读模式实现**（依据 [ReadingMode.kt](https://github.com/mihonapp/mihon/blob/main/app/src/main/java/eu/kanade/tachiyomi/ui/reader/setting/ReadingMode.kt) 与 [Reader 设置文档](https://mihon.app/docs/guides/reader-settings/)）：
- 六种模式：**默认（按全局设置）、从左到右、从右到左（默认，RTL）、垂直（分页）、Webtoon（连续）、长条带间隔（ContinuousVertical）**；阅读模式可按“单部作品”覆盖（per-manga override），全局与单部两级配置（[DeepWiki 阅读器配置](https://deepwiki.com/mihonapp/mihon/4.3-reader-configuration-and-preferences)）。
- 双页/跨页处理：`dualPageView` 三档 **Never/Always/Wide**（宽图才双页）；“拆分宽页”（split wide pages，把横向跨页拆成两页）+ 反转选项；“旋转宽页以适配”（rotate to fit，Paged 与 Webtoon 各有独立开关）。
- 缩放：六种 scale type（**Fit screen / Stretch / Fit width / Fit height / Original size / Smart fit**）；缩放起始焦点可设（自动/左/右/中，RTL 场景默认关注右上角）；双击缩放动画速度可调；webtoon 双击缩放开关。
- 过渡与显示：翻页过渡动画开关；每章过渡页；显示页号；阅读背景色（黑/灰/白/自动）；全屏/刘海（cutout）模式（Ignore/Avoid/Shift）；保持屏幕常亮。
- 滤镜与主题：软件亮度（-75~100）、RGB(A) 颜色滤镜（位掩码 + Multiply/Screen/Overlay/Lighten/Darken 混合模式）、灰度、反色；亮/暗主题。
- 墨水屏适配：**E-ink 模式**（翻页闪烁刷新，可选黑/白闪，间隔可调）——对低刷新屏很有借鉴意义。
- 导航：Pager 与 Webtoon 分别配置点击区（默认/L 形/Kindle 式/边缘/左右/禁用），可水平/垂直反转；音量键翻页（可反转）；长按快捷操作（设封面/复制/分享/保存）；裁剪边框（crop borders）选项。

**本地库/文件管理**：分类（Categories）组织库；定时“智能更新”（Smart updates）与计划更新；下载管理（章节下载、按系列目录组织）；**备份/恢复**（JSON 格式，含库条目、章节数据、跟踪、历史、分类、应用设置与扩展源配置；默认不含下载文件/扩展本体；可用 [Mihon Backup Viewer](https://github.com/mihonapp/mihon-backup-viewer) 在外部查看编辑，可跨 fork 恢复，[备份文档](https://mihon.app/docs/guides/backups/)）；阅读历史；跟踪服务集成（MyAnimeList、AniList、Kitsu、Shikimori、Bangumi、MangaUpdates、MangaBaka、Hikka）。

**许可证与活跃度**：Apache-2.0；22,926 stars / 1,424 forks；最新 v0.20.4（2026-08-05），仓库 2026-08-17 仍有推送，非常活跃；要求 Android 8.0+。

**架构亮点**：
- 阅读器采用 **Viewer 抽象**（PagerViewer 系列 vs WebtoonViewer vs WebGpuViewer），阅读模式与渲染后端解耦，可插拔替换；
- 迁移方向是 Compose + 模块化（`presentation-core` 抽取公共 Compose 组件、`tachiyomi-core`/`tachiyomi-i18n` 分层），数据库/网络/解析各层独立；
- 高质量渲染器（WebGPU）与默认渲染器并存，给“画质/性能”留了实验空间；
- 备份格式与官方查看工具配套，生态可迁移。

### 2. Kotatsu（含 Kotatsu-Redo）

**背景**：Kotatsu 是 Android 上另一著名开源阅读器，以“内置在线目录聚合（1200+ 源）+ 下载离线 + 本地 CBZ”为特色。**2025 年底项目宣布停运**：README 顶部公告称因 Kakao Entertainment 的威胁性法律行动与 Google 新侧载政策，决定终止开发，仓库已 archived（最后推送 2025-11-04）。社区 fork **Kotatsu-Redo**（[github.com/Kotatsu-Redo/Kotatsu-Redo](https://github.com/Kotatsu-Redo/Kotatsu-Redo)，810 stars）继续维护，2026-08 仍活跃。

**技术栈**（[libs.versions.toml](https://github.com/KotatsuApp/Kotatsu/blob/devel/gradle/libs.versions.toml)）：Kotlin 2.2 + 传统 View（RecyclerView + adapter-delegates，**未用 Compose**）；图片 Coil 3（SVG/GIF）；DB 用 **Room**；DI 用 Hilt；缩放用自 fork 的 subsampling-scale-image-view；**AVIF 解码**（org.aomedia.avif.android）；DiskLruCache、Moshi、OkHttp、Markwon；在线解析器独立成 [kotatsu-parsers](https://github.com/KotatsuApp/kotatsu-parsers) 仓库。

**格式与阅读**：下载章节离线阅读；支持第三方 CBZ 本地档案；标准阅读器与 Webtoon 优化阅读器，可定制、支持手势；Material You 界面，适配手机/平板/桌面（大屏窗口）。

**本地库/文件管理**：收藏分类、阅读历史、书签、隐身模式、新章通知与更新流、推荐（带过滤）、跟踪（Shikimori/AniList/MAL/Kitsu）、密码/指纹锁、同账号多设备数据同步、Android 6.0+。

**许可证与活跃度**：GPL-3.0；8,811 stars；官方版已停运，Redo fork 活跃。

**架构亮点**：在线目录（parsers）与 App 分离的仓库结构，解析器可独立演进；一体化的“搜索→收藏→下载→离线→跟踪→多设备同步”闭环是它最大的产品形态亮点；对“本地优先”的我们而言，其**多设备同步 + 目录聚合**思路可借鉴（但我们的核心是本地库，在线源非重点）。

### 3. YACReader（Windows/macOS/Linux）

**定位**：跨平台桌面漫画阅读器 + 库管理器（YACReaderLibrary）+ 可选的局域网服务器（YACReaderLibraryServer），另有闭源的 iOS App（[App Store](https://apps.apple.com/app/yacreader-comic-reader/id635717885)）与 Android 远程浏览端（[YACReader Remote](https://play.google.com/store/apps/details?id=com.yacreader.yacreader)）。官网 [yacreader.com](https://www.yacreader.com/)。

**技术栈**：**C++20 + Qt 6.9+**、CMake 构建、Azure Pipelines CI；7-Zip 解压后端（CMake 时自动下载）；渲染从 OpenGL 迁移到 **QRhi**（Qt 渲染硬件接口，跨平台兼容性更好）；10.2 起引入主题系统（12 套内置 + 自定义主题编辑器）。

**支持的格式**（FreeBSD man page 与官网）：RAR、ZIP、CBR、CBZ、TAR、PDF（含针对竖长图 webtoon 的渲染优化）、EPUB（10.2 实验性，仅图片型漫画）；图片 JPEG/GIF/PNG/TIFF/WebP/BMP；支持用户安装 Qt 图像格式插件扩展格式。Windows 10.1 起支持长路径（需系统开启 LongPathsEnabled）。

**阅读模式实现**：双页模式；可定制放大镜（10.2 新增**圆形放大镜+描边**、边缘缓动，可在矩形/圆形间切换）；连续滚动模式（10.0 引入，10.1 优化性能与减少闪烁）；页面旋转；全尺寸/全屏视图；可自定义背景色；书签与续读；“go-to-flow”花式页面浏览；捏合/触控板 + Ctrl+滚轮缩放；**Lanczos 插值**（缩放算法可在设置中切换）；按 fit 模式切换后不丢阅读位置（10.1 修复）；支持自定义缩放快捷键（[10.2 发布说明](https://www.yacreader.com/)）。

**本地库/文件管理**：YACReaderLibrary 提供库管理（元数据编辑含批量、封面管理、搜索语法引擎与快速预设、按“通用数字”智能排序）；10.2 新增**库修复**（恢复丢失封面、重新扫描失败文件）、**数据库备份/恢复**（自动保留在 .yacreaderlibrary/backups，检测到损坏时提议修复或恢复，损坏原件保留到 recovery 目录）；YACReaderLibraryServer 提供 /webui 管理界面、基础网页阅读器、HTTP API 与命令行（backup-library/list-backups/restore-library/repair-library-db）。iOS 端有面板级阅读（panel-by-panel 自动检测分镜）、自动页边裁剪、离线实时翻译（iOS 18+，设备端 OCR+翻译）。

**许可证与活跃度**：GPL-3.0；1,350 stars；最新 10.2.0（2026-08-08），仓库活跃；开发由作者 Luis Ángel San Martín 单人主导（个人化治理，贡献需先邮件沟通）。

**架构亮点**：桌面端“阅读器 + 库 + 局域网服务器 + WebUI + 手机远程端”的完整分层，是**“本地库如何向手机端延伸”的最佳桌面样本**；10.x 的“主题系统 + QRhi + Lanczos”说明老牌项目也在持续打磨渲染质量；库的备份/修复体系很完善。
### 4. Komikku（GNOME）

**定位**：GNOME 桌面的漫画阅读器（GNOME Circle 应用），在线源 + 离线阅读，主要面向 Linux 用户，通过 Flatpak 分发（[Flathub: info.febvre.Komikku](https://flathub.org/apps/info.febvre.Komikku)）。

**技术栈**：**Python + GTK3 + libhandy**（自适应界面）、Meson 构建；依赖 beautifulsoup4/lxml（HTML 解析）、brotli、cloudscraper（绕过反爬）、natsort（自然排序）、Pillow、keyring、python-protobuf 等。仓库原在 GitLab（valos/Komikku），现迁移至 **Codeberg**（[codeberg.org/valos/Komikku](https://codeberg.org/valos/Komikku)，Python 约 230 万行，含 Meson 构建）。

**支持的格式**：在线下载章节 + 离线阅读（自身下载格式）；本地文件导入能力较弱（非主打）。

**阅读模式实现**：明确支持 **RTL、LTR、Vertical、Webtoon** 四种阅读模式；导航方式非常丰富：键盘方向键、鼠标点击左右区域（左右布局可配置）、鼠标滚轮、触控板双指滑动、触屏滑动——桌面端多输入方式的典范；亮/暗主题。

**本地库/文件管理**：分类组织库；自动更新漫画；自动下载新章节；阅读历史；离线阅读下载内容。

**许可证与活跃度**：GPL-3.0+；Codeberg 106 stars（Stars 少但生态健康，Void Linux 等发行版持续打包，2025 年更新到 1.84.0，仓库 2026-08 仍有推送，活跃）。

**架构亮点**：多阅读方向 + 多输入导航的桌面范式；对 GNOME 设计语言（libhandy 自适应、跟随系统主题）的深度集成；Python/GTK 技术栈开发门槛低，适合小团队维护。

### 5. OpenComic

**定位**：跨平台（Windows/macOS/Linux）漫画 + 电子书阅读器，基于 Electron，发布渠道覆盖 MS Store、Mac App Store、Flathub、Snap、AUR 等（[github.com/ollm/OpenComic](https://github.com/ollm/OpenComic)，官网 [opencomic.app](https://opencomic.app)）。

**技术栈**（[package.json](https://github.com/ollm/OpenComic/blob/master/package.json)）：Node.js + **Electron 43**，核心逻辑 TypeScript，UI 为 jQuery 模板；图像处理用 **sharp（libvips）**；压缩解压用 **node-7z + 7zip-bin-full（7-Zip 二进制）** + zstd；PDF 用 **pdfjs-dist**；EPUB 用 **epubjs + foliate-js**；JPEG-XR 用 jpegxr；**AI 能力**：onnxruntime-node + opencomic-ai-bin（AI 超分、去网点、去压缩伪影、黑白上色）。

**支持的格式**（README 特性清单）：图片 JPG/JP2/JXR/JXL/PNG/APNG/AVIF/WEBP/GIF/SVG/BMP/ICO；压缩 RAR/ZIP/7Z/TAR/LZH/ACE/CBR/CBZ/CBA/CB7/CBT；PDF、EPUB（Alpha）；**远程库协议**：SMB、FTP(S)、SCP、SFTP、SSH、S3、WebDAV；OPDS；主文件夹（master folders）管理；背景音乐播放（MP3/M4A/OGG/FLAC 等）。

**阅读模式实现**：Manga（RTL）模式、Webtoon 模式、双页视图、书签/续读、**浮动放大镜**、滚动或翻页阅读；亮度/饱和度/对比度/棕褐/反相/负片滤镜与黑白上色；**多种插值方法可选（lanczos3/lanczos2/mitchell/cubic/linear/nearest）**；自定义快捷键与点击区、手柄（gamepad）导航；收藏/自定义标签。

**本地库/文件管理**：以“主文件夹”为库的扫描式管理 + 标签体系；通过 SMB/FTP/SFTP/S3/WebDAV 连接 NAS/服务器远程读库；OPDS 目录支持。

**许可证与活跃度**：GPL-3.0；1,946 stars；最新 v1.6.5（2025-10-31，仓库内版本号已至 1.6.6），2026-08 仍活跃推送。

**架构亮点**：**格式支持面最广**（含 AVIF/JXL/JP2/JXR/LZH/ACE 等冷门格式，Windows 平台目标强烈契合）；用 7-Zip 外部二进制解决压缩兼容性，用 pdfjs/epubjs 解决文档格式；Electron 生态使其能快速覆盖三平台与商店分发；AI 图像增强是其差异化卖点。

### 6. Houdoku

**定位**：桌面（Windows/macOS/Linux）漫画阅读器 + 库管理器，在线源与本地导入并存（[github.com/xgi/houdoku](https://github.com/xgi/houdoku)，官网 [houdoku.org](https://houdoku.org)）。

**技术栈**（README）：Electron；UI 用 **React + Radix/shadcn** 基础组件；状态管理 Recoil；库数据与设置存 **localStorage**（缩略图在 user-data 目录）；扩展走 **Tiyo** 插件体系（[github.com/xgi/tiyo](https://github.com/xgi/tiyo)，用 aki-plugin-manager 动态加载）。

**支持的格式**：本地导入 ZIP 或图片文件夹；在线源经 Tiyo 插件（相当于 Tachiyomi 的扩展思路）。

**阅读模式实现**（[Customize 指南](https://houdoku.org/guides/customize.html)）：三种页面布局：**单页、双页、长条（vertical）**；双页/长条可加页间距，**双页可设置 offset（模拟实体书左右页错位，保证 RTL 时左右页正确配对）**；阅读方向 LTR/RTL（影响侧栏按钮与快捷键语义）；图像尺寸限制（限宽/限高）；可自定义阅读器快捷键（翻页、换章、布局等）。

**本地库/文件管理**：标签/过滤管理大库；下载章节离线阅读；跟踪器支持；备份/恢复库。

**许可证与活跃度**：MIT；1,107 stars；最新 v2.16.0（2024-12-17），最后推送 2025-01，**开发明显放缓**（单一维护者）。

**架构亮点**：阅读器定制模型极简清晰（布局×方向×间距×offset×尺寸限制），文档化良好；Tiyo 插件化的内容源扩展与 Tachiyomi 思路同源但更轻。
### 7. MComix（经典）

**定位**：经典 Linux/跨平台漫画查看器，Comix 的接续 fork（Comix 原开发者停止维护后由社区接手），面向“快速看图”的轻量工具（[SourceForge 项目页](https://sourceforge.net/projects/mcomix/)）。

**技术栈**：**Python + GTK3**（PyGObject/cairo）、Pillow（图像）、poppler（PDF 渲染）、7-Zip 后端（解压）；GPL-2.0（[Void Linux 打包模板](https://github.com/void-linux/void-packages/blob/master/srcpkgs/mcomix/template)确认依赖与许可证）；GitHub 上有镜像 [multiSnow/mcomix3](https://github.com/multiSnow/mcomix3)（95 stars，含 Lanczos 重采样等改进）。

**支持的格式**：CBR、CBZ、**CB7（7z）**、CBT（tar）、LHA、PDF；ZIP/RAR/7Z/LHA 直接读取（不依赖外部解压后目录）。

**阅读模式实现**：全屏模式；**双页模式**；适应窗口宽度/高度/两者；**智能滚动**（连续按空格滚动阅读）；**Manga 模式（RTL）**；书签与库（library）；页面旋转；缩略图导航；每本书可记忆显示设置（MComix 支持 per-book 配置：适应方式、RTL、双页等）。

**本地库/文件管理**：以文件浏览为主的轻量库；书签；历史记录。

**许可证与活跃度**：GPL-2.0；最新 3.1.1（2025-09-06），SourceForge 项目页显示 2026-06 仍有更新、下载量 1,066/周——**用户盘子还在，但开发节奏慢、功能停滞**。

**架构亮点**：极简键盘驱动的阅读效率（智能滚动、快捷键）；多容器格式直读；是“小而美”路线的代表，适合对照其功能清单做减法参考。

### 8. MangaMeeya（经典，专有）

**定位**：Windows 上经典的本地漫画阅读器（日本开发，免费但不开源），2000 年代末至 2011 年风靡中文社区（多汉化版），最终版 **7.4**（约 2011 年）。

**特点**（[社区介绍](https://baka.li/mangameeya/)、[百度百科](https://baike.baidu.com/item/MangaMeeya)）：**预读取/缓存**使得翻页极快；**RAR/ZIP 压缩包直读**（不支持 7Z）；强大的快捷键设置；书架（bookshelf）管理收藏；双页等多种视图；对当时大量“RAR 分包漫画”的本地阅读体验近乎标杆。

**历史教训**：作者因**违反 GPL 协议**（使用了 GPL 代码却未开源衍生作品）而停止更新——这是漫画阅读器领域最著名的许可证警示案例；对我们而言意味着：**引用任何 GPL/AGPL 组件（如某些渲染/解压库）时必须严格遵守其许可证，或从一开始就用宽松许可证（Apache-2.0/MIT）的组件**。

### 9. 自托管漫画服务器：Kavita、Komga、Calibre-Web

#### 9.1 Kavita

**定位**：自托管阅读服务器（.NET），漫画 + 电子书一站式，跨平台（Windows/Linux/macOS/Docker/NAS），官方定位“全解决方案”（[github.com/Kareadita/Kavita](https://github.com/Kareadita/Kavita)，[Wiki](https://wiki.kavitareader.com/)）。

**技术栈**：**C#/.NET 后端 + Angular（Kavita-webui）前端**，图像处理 NetVips；Kavita+ 付费订阅提供元数据/评分/评论 Scrobbling（同步 MAL/AniList）、智能收藏等增值能力（[Kavita+ 文档](https://wiki.kavitareader.com/kavita+)）。

**支持的格式**：漫画/Webtoon：CBR、CBZ、ZIP、RAR、RAR5、7z、原始图片；书籍：EPUB、PDF；库类型：Manga、Comic（Flexible）、Comic、EPUB、PDF、Images（决定解析策略）。注意：**Kavita 不镜像文件夹结构**，用“文件名 + 内部元数据（如 ComicInfo.xml，仅 c 开头扩展名档案会被检查）”解析（[FAQ](https://wiki.kavitareader.com/troubleshooting/faq/)）。

**阅读模式实现**：响应式网页阅读器（手机/平板/桌面自适应）；**Webtoon 连续模式**；**连续阅读**（章节结束不离开阅读器）；EPUB 虚拟页；阅读方向 LTR/RTL（讨论中的“按库默认阅读方向” [#2537](https://github.com/Kareadita/Kavita/discussions/2537)）；**双页布局 + offset**（源码含 fit-to-height-double-offset）；主题系统（自定义主题仓库）；阅读档案（Reading Profiles）；EPUB 批注/高亮。

**本地库/文件管理**：丰富元数据（ComicInfo.xml、EPUB OPF、PDF 元数据等）+ 智能过滤/搜索/收藏；集合、阅读列表（**CBL 导入**）、想看列表；用户系统（RBAC 角色、OIDC、年龄限制）；进度跟踪（按用户）；文件扫描 + 定时任务；无官方移动 App（官方建议 PWA 添加到主屏幕），但有 3rd party 专用客户端：**Inkita**（Android）、**Kover**（跨平台，离线阅读 + 进度同步）、**Turnleaf**（离线 EPUB，Android）、**Kamigura**（Android 平板优先）、KOReader 插件 Kamare；以及通用客户端：Mihon、Yomu、Aidoku、Panels、Paperback、Komic、CDisplayEX、Mylar（[Wiki 3rd Party Clients](https://wiki.kavitareader.com/guides/3rdparty/)）。

**许可证与活跃度**：GPL-3.0；11,492 stars；最新 v0.9.0.2（2026-05-14，官方仍标注 beta），仓库非常活跃（2026-08-17 推送）。

**架构亮点**：元数据驱动的智能库（过滤/收藏/阅读列表/智能收藏）；**订阅增值模式（Kavita+）**为开源项目提供可持续收入；**PWA 移动端 + 3rd party 客户端生态**是“服务器→手机端”的低成本路径。

#### 9.2 Komga

**定位**：自托管媒体服务器（漫画/BD/杂志/电子书），JVM 应用，跨平台（Windows/Linux/macOS/Docker），以“API 开放 + 生态丰富”著称（[github.com/gotson/komga](https://github.com/gotson/komga)，文档 [komga.org](https://komga.org)）。

**技术栈**：**Kotlin + Spring Boot 后端 + Vue 3/TypeScript Web UI**（komga-webui）；按需原生库解码（JXL/AVIF/HEIF）；大量测试与 CI（[DEVELOPING](https://github.com/gotson/komga/blob/master/DEVELOPING.md)）。

**支持的格式**（[Image Types 文档](https://komga.org/docs/guides/image-formats/)）：存档 ZIP/CBZ、RAR/CBR、7Z；文档 PDF、EPUB；图片 JPEG/PNG/GIF/WebP（全平台），JPEG XL/AVIF/HEIF（macOS App、Windows App、Docker amd64/arm64 原生支持；arm 上 WebP 用 Java 实现）。**按需转码**：当客户端浏览器不支持的图片格式（如 WEBP 老浏览器）时自动转码（如 WEBP→JPEG）；分析阶段收集容器/图片格式/页尺寸，用于智能缩略图与转码决策。

**阅读模式实现**（[Webreader: DIVINA](https://komga.org/docs/guides/webreader-divina/)）：内置 **DIVINA** Web 阅读器，四种模式 **LTR / RTL / Vertical / Webtoon（连续竖条）**；**自动使用书籍元数据中指定的阅读方向**（ComicInfo.xml 的 Manga 标签）；翻页过渡动画开关；触屏手势（仅分页模式）；背景色（白/灰/黑）；缩放类型（Fit to screen / Fit to width / Fit to height / Original）；**双页模式**（如实体书摊开，但**首尾页、横向页强制单页**）；缩略图浏览器快速导航；上下文相关的快捷键帮助。另有 EPUB Web 阅读器。

**本地库/文件管理**：库=文件夹，**每个子文件夹（任意深度）= 系列（Series），每个文件 = 书（Book）**；扫描/深度扫描（增量基于 mtime）；**元数据**：CBZ/CBR 内 ComicInfo.xml、EPUB 元数据、本地 art assets（封面/图集）——均可手动编辑与批量刷新（[扫描与元数据文档](https://komga.org/docs/guides/scan-analysis-refresh/)）；**重复文件检测、重复页面检测（按页 hash）与移除**；书导入（move/copy/hardlink，可升级替换同名编号书）；全文搜索；收藏、阅读列表（**CBL 导入**）；**每用户阅读进度**（未读/在读/已读，[Read progress](https://komga.org/docs/guides/read-progress/)）；多用户 + 按库 ACL + 年龄限制 + 标签限制。

**API 与客户端生态**：完整 **REST API（OpenAPI 文档）** + **OPDS v1.2 / v2**（[OPDS 文档](https://komga.org/docs/guides/opds/)）；Kobo Sync、KOReader Sync；**官方 Mihon 扩展**（托管于 Keiyoushi 扩展仓库，可配最多 10 台服务器，浏览/搜索/过滤，且**阅读进度与 Komga 双向同步**，[Read with Mihon](https://komga.org/docs/guides/mihon/)）；已测试客户端：**Komelia**（Android，[F-Droid](https://f-droid.org/packages/io.github.snd_r.komelia/)/[GitHub](https://github.com/Snd-R/Komelia)）、Yomu、Panels（iOS）、Paperback（iOS）、Chunky（iPad）、Komic、MangaBox、KMReader、Koharia、CDisplayEx、Kobo、KOReader、Aldiko/Moon+/Librera（OPDS v1）。

**许可证与活跃度**：MIT（对商业/闭源再集成友好）；6,571 stars；最新 1.26.3（2026-08-12），仓库非常活跃。

**架构亮点**：**服务器只存“事实”，一切通过 API/OPDS 暴露**，客户端（官方 Web 阅读器 + 官方 Mihon 扩展 + 第三方客户端）都消费同一套协议——这是“为未来手机端铺路”的最佳范本；阅读方向、双页规则从元数据自动推断；按需转码解决格式兼容；重复页检测是独有亮点。

#### 9.3 Calibre-Web

**定位**：电子书库 Web 管理界面（依托 Calibre 的 metadata.db），浏览/阅读/下载 + 用户管理 + OPDS（[github.com/janeczku/calibre-web](https://github.com/janeczku/calibre-web)）。

**技术栈**：**Python Flask + Jinja2 + Bootstrap 3**；封面提取用 ImageMagick（EPUB）/Ghostscript（PDF）；Kobo 同步需要 kepubify；Docker 镜像 linuxserver/calibre-web。

**功能要点**（README）：响应式界面；图形化安装；细粒度用户权限管理 + 管理后台；20+ 语言；**OPDS 目录**（供阅读器 App 使用）；高级搜索/过滤；自定义书架（shelves）；元数据编辑/删除与插件化元数据下载；调用 Calibre 二进制转换格式；一键发送到 e-reader；**Kobo 同步**；浏览器内阅读（EPUB/PDF/TXT）；上传（含音频）；自定义列；按用户隐藏内容；Magic Link 登录；LDAP/Google/GitHub OAuth/代理认证。

**对漫画的定位**：Calibre-Web 本质是电子书库，漫画（CBZ 等）可入库、下载、经 OPDS 传输，但**浏览器内阅读体验与漫画阅读器（RTL/双页/webtoon）无关**；它在“元数据管理、权限、OPDS 服务端”上的成熟度值得借鉴，但阅读引擎不是它的战场。

**许可证与活跃度**：GPL-3.0；17,880 stars；最新 0.6.27（2026-08-08），非常活跃；PyPI 发布（calibreweb）。
## 值得借鉴的机制清单

### 阅读引擎

1. **多阅读方向 + RTL 默认**：Mihon 默认“Paged (right to left)”，并提供 LTR/RTL/Vertical/Webtoon/Continuous 五档；Komikku 四档；Komga DIVINA 四档且**从 ComicInfo.xml 的 Manga 标签自动推断方向**。→ 我们的核心场景是日漫 RTL，应内置“全局默认 + 每书/每系列覆盖”两级配置（Mihon 的 per-manga override）。
2. **双页模式的细节规范**：Mihon 的 dualPageView 三档（Never/Always/Wide）、Komga 的“**首尾页单页、横向页单页**”规则、Houdoku/Kavita 的**双页 offset（模拟实体书左右页错位）**、Mihon 的“拆分宽页 / 旋转宽页以适配”。→ 双页不是简单并排，中缝/封面/横向页的处理规则必须显式设计。
3. **宽页（跨页图）处理**：split wide pages（Mihon，Paged 与 Webtoon 各带反转开关）、rotate to fit（Mihon）、landscape 单页显示（Komga）。
4. **连续阅读与 Webtoon**：Kavita 的“连续阅读（不离开阅读器）”、Mihon 的 Long strip / Long strip with gaps、webtoon 侧边距可调（0-25）、隐藏菜单灵敏度分级。
5. **缩放体系**：六档 scale type（Fit screen/Stretch/Fit width/Fit height/Original/Smart fit，Mihon）、缩放起始焦点（RTL 场景默认右上，Mihon zoom start）、双击缩放动画速度、**插值算法可选（Lanczos3/2、Mitchell、Cubic、Linear、Nearest——OpenComic/YACReader/MComix3）**。
6. **过渡动画分层**：Mihon 传统渲染器“平滑过渡开关”，WebGPU 渲染器提供 11 种 3D 翻页动画（Flip/Stack/Sphere/Cube/Fade）；Komga 也有动画开关。→ 动画应是可选且可关闭的。
7. **滤镜与阅读舒适度**：软件亮度（可低于硬件最低亮度，Mihon）、RGBA 颜色滤镜 + 混合模式（Multiply/Screen/Overlay 等）、灰度、反色、棕褐（OpenComic）；阅读背景色黑/灰/白/自动（Mihon/Komga）。
8. **E-ink/低刷新屏适配**：Mihon 的翻页闪烁（黑/白闪、间隔可调）——对未来墨水屏设备或 Windows 上低刷屏有直接价值。
9. **导航方案**：tap zones 多预设（默认/L 形/Kindle 式/边缘/左右/禁用）+ 水平/垂直反转（Mihon）、音量键翻页（可反转）、长按快捷操作、手柄导航（OpenComic）、键盘快捷键体系（YACReader/Houdoku/MComix）。
10. **阅读辅助**：放大镜（YACReader 矩形/圆形+描边+边缘缓动；OpenComic 浮动放大镜）、面板级阅读 panel-by-panel（YACReader iOS，自动分镜检测）、智能滚动（MComix）、go-to-flow 页面流浏览（YACReader）。
11. **每库/每书阅读默认**：Kavita 社区强烈要求的“按库默认阅读方向/Webtoon 模式”（[#2537](https://github.com/Kareadita/Kavita/discussions/2537)）——库类型化（Manga/Webtoon/Comic）驱动阅读器默认值，是省心设计。

### 本地库与文件管理

1. **目录约定**：Mihon 的 `local/[系列]/[章节]/图片`（压缩包名=章节名，包内结构忽略）——零数据库也能工作；Kavita 与 Komga 则相反（服务器建索引）。Windows 本地优先场景可两者结合：目录约定兜底 + 索引加速。
2. **元数据标准**：**ComicInfo.xml**（ComicRack 标准）被 Komga/Kavita 原生解析（含 Manga 标签→阅读方向）；EPUB OPF/PDF 元数据；封面/art assets 本地化（Komga local artwork）。
3. **扫描与增量**：Komga 的“扫描（mtime 增量）/ 深度扫描 / 分析（容器+图片格式+页尺寸）/ 元数据刷新”四层模型；Kavita 的定时扫描 + inotify；Mihon 的 pull-to-refresh 手动刷新。
4. **按需转码**：Komga 根据客户端能力把不支持的图片格式（如 WEBP）实时转码——本地阅读器可类比“低内存设备自动降采样”。
5. **去重**：Komga 的重复文件检测 + **重复页 hash 检测与移除**。
6. **整理/导入**：Komga 的导入（move/copy/hardlink、升级替换）、编号书升级；Calibre-Web 的转换链。
7. **进度与同步**：每用户阅读进度（Komga/Kavita）、双向同步（Komga↔Mihon 扩展、Kavita↔Kover/Kamare）；书签/续读/历史是标配。
8. **备份/恢复可移植**：Mihon 的 JSON 备份（可跨 fork 恢复 + 官方 Backup Viewer）；YACReader 的库数据库自动备份/修复。
9. **权限模型**：Komga/Kavita/Calibre-Web 的用户/角色/年龄/标签限制——为“未来手机端多人共享”预留。
10. **细节**：Mihon 建议 local 目录放 `.nomedia`（防止图片进系统相册）——小但贴心。

### 架构

1. **阅读器可插拔 Viewer 架构**：Mihon 把阅读模式（Pager/Webtoon/WebGPU）抽象为可替换 Viewer，渲染后端独立演进（实验性 WebGPU 渲染器 + 3D 动画）。
2. **服务器 + API/OPDS 的开放生态**：Komga（REST + OPDS v1/v2 + 官方 Mihon 扩展 + 10+ 第三方客户端）与 Kavita（PWA + 专用 App + Mihon/Yomu/Aidoku 等）证明：**“核心库+进度在服务器，阅读器任意换”是覆盖手机端的成本最低路径**。
3. **解析器/扩展仓库分离**：kotatsu-parsers、Keiyoushi extensions、Houdoku 的 Tiyo——内容源与 App 解耦，社区可独立维护。
4. **图片管线**：Coil3 + OkHttp（brotli/zstd）+ DiskLruCache + subsampling-scale-image-view 是 Android 双雄的共同选型；Electron 侧用 sharp/libvips + 7-Zip 二进制 + pdfjs/epubjs。
5. **压缩抽象**：libarchive（Mihon，跨格式统一）、7-Zip 外部二进制（YACReader/OpenComic/MComix）——**RAR 的解码是版权/专利敏感点，用 7-Zip 二进制或 unrar 需注意许可**（MangaMeeya 的 GPL 违规教训）。
6. **渲染层**：Qt QRhi（YACReader）、WebGPU（Mihon 实验）、libvips/sharp（OpenComic）——Windows 优先应尽早确定硬件加速渲染路径。
7. **i18n**：主流项目全部接入 Weblate 众包翻译；moko-resources（Mihon）做资源多平台化。
8. **可持续性**：Kavita+ 订阅增值、YACReader/OpenComic 的捐赠/Patreon、Mihon 的开源团队治理——开源阅读器长期维护是个现实问题（Kotatsu 停运、Houdoku 放缓都是先例）。

## 对我们项目的启示

1. **技术路线参考**：Windows 优先的三条已验证路线——(a) Qt/C++（YACReader：性能好、单文件分发成熟，但开发慢）；(b) Electron/Web 技术（OpenComic、Houdoku：迭代快、三平台+商店分发容易，但内存/包体大）；(c) Rust/Tauri 或 Flutter 的现代混合路线（无直接漫画竞品，但可作为差异化选项）。若未来做 iOS/Android，**共享阅读引擎 + 每端原生壳**（参考 Mihon 的 Compose 全栈 vs Kotatsu 传统 View）比 Electron 套壳更值得投入。
2. **阅读引擎是核心护城河**：把 RTL 默认、双页细节（首尾页/横向页/offset）、Webtoon 连续、宽页拆分/旋转、缩放插值、过渡动画开关、滤镜、E-ink 适配做深做细——这正是竞品差异最大、用户感知最强的部分。
3. **本地库设计**：采用“目录约定兜底 + ComicInfo.xml 元数据 + 索引缓存”的组合；提供书签/续读/历史/进度，并设计可导出/可迁移的进度格式（Mihon 备份思路）。
4. **为手机端留好接口**：即使第一期只做 Windows 单机，也应在架构上预留“本地 HTTP 服务 + OPDS/轻量 API”（YACReaderServer/Komga 模式），未来手机端即可零成本接入；Kavita/Komga 证明这是最稳的移动化路径。
5. **许可证策略**：核心代码用 Apache-2.0/MIT（对标 Mihon/Komga/Houdoku），避免 GPL 传染；引入 RAR 解码等组件时核查许可（7-Zip LGPL/unRAR 限制）；不要在 README/公告上重蹈 Kotatsu 因法律威胁停运的覆辙——本项目是纯本地阅读器，不聚合在线源，天然规避版权风险。
6. **生态开放性**：尽早定义扩展点（格式插件、元数据源、未来在线源），学习 Komga 的“官方客户端 + 开放 API + 第三方生态”模式，用社区力量覆盖长尾格式与平台。

## 主要来源链接

- Mihon：[仓库](https://github.com/mihonapp/mihon) / [Local source 文档](https://mihon.app/docs/guides/local-source/) / [Reader 设置文档](https://mihon.app/docs/guides/reader-settings/) / [备份文档](https://mihon.app/docs/guides/backups/) / [DeepWiki 阅读器配置](https://deepwiki.com/mihonapp/mihon/4.3-reader-configuration-and-preferences) / [ReadingMode.kt](https://github.com/mihonapp/mihon/blob/main/app/src/main/java/eu/kanade/tachiyomi/ui/reader/setting/ReadingMode.kt)
- Tachiyomi 停运：[Alternativeto 报道](https://alternativeto.net/news/2024/1/manga-reader-app-tachiyomi-ceases-development-amid-legal-threats-from-kakao-entertainment/)
- Kotatsu：[仓库（含停运公告）](https://github.com/KotatsuApp/Kotatsu) / [Kotatsu-Redo](https://github.com/Kotatsu-Redo/Kotatsu-Redo) / [kotatsu-parsers](https://github.com/KotatsuApp/kotatsu-parsers)
- YACReader：[仓库](https://github.com/YACReader/yacreader) / [官网（10.2 发布说明）](https://www.yacreader.com/) / [iOS App](https://apps.apple.com/app/yacreader-comic-reader/id635717885) / [YACReader Remote](https://play.google.com/store/apps/details?id=com.yacreader.yacreader) / [10.1.0 Release](https://github.com/YACReader/yacreader/releases/tag/10.1.0)
- Komikku：[Codeberg 仓库](https://codeberg.org/valos/Komikku) / [Flathub](https://flathub.org/apps/info.febvre.Komikku) / [GNOME Apps 页面](https://teams.pages.gitlab.gnome.org/Websites/apps.gnome.org/Komikku/)
- OpenComic：[仓库](https://github.com/ollm/OpenComic) / [官网](https://opencomic.app) / [package.json](https://github.com/ollm/OpenComic/blob/master/package.json)
- Houdoku：[仓库](https://github.com/xgi/houdoku) / [Customize 指南](https://houdoku.org/guides/customize.html) / [Tiyo 插件](https://github.com/xgi/tiyo)
- MComix：[SourceForge](https://sourceforge.net/projects/mcomix/) / [GitHub 镜像 mcomix3](https://github.com/multiSnow/mcomix3)
- MangaMeeya：[社区介绍](https://baka.li/mangameeya/) / [百度百科](https://baike.baidu.com/item/MangaMeeya)
- Kavita：[仓库](https://github.com/Kareadita/Kavita) / [Wiki](https://wiki.kavitareader.com/) / [FAQ](https://wiki.kavitareader.com/troubleshooting/faq/) / [专用 App 文档](https://wiki.kavitareader.com/guides/3rdparty/kavita-dedicated/) / [Kavita Reader (Google Play)](https://play.google.com/store/apps/details?id=com.hesshomestead.reader) / [讨论 #2537](https://github.com/Kareadita/Kavita/discussions/2537)
- Komga：[仓库](https://github.com/gotson/komga) / [DIVINA Webreader](https://komga.org/docs/guides/webreader-divina/) / [Read with Mihon](https://komga.org/docs/guides/mihon/) / [Image Types](https://komga.org/docs/guides/image-formats/) / [扫描与元数据](https://komga.org/docs/guides/scan-analysis-refresh/) / [OPDS](https://komga.org/docs/guides/opds/) / [Komelia (F-Droid)](https://f-droid.org/packages/io.github.snd_r.komelia/)
- Calibre-Web：[仓库](https://github.com/janeczku/calibre-web) / [PyPI](https://pypi.org/project/calibreweb/)
- 对比参考：[Kavita vs Komga](https://selfhosting.sh/compare/kavita-vs-komga/) / [Komga vs Kavita vs Calibre-Web](https://www.bigiron.cc/guides/komga-vs-kavita-vs-calibre-web)