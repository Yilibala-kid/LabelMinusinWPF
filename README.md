# LabelMinus

LabelMinus 是一个基于 C# / WPF 的轻量级（>150M）图像标注与翻译辅助工具，兼容 LabelPlus 翻译文本的导入与导出。它在基础标注流程之外，加入了图校、OCR、自动保存、右键打开、深浅模式等面向日常嵌字/校对工作的功能。

项目仍在持续开发中，界面和功能可能随版本调整。

## 简介

LabelMinus 适合用来处理漫画、图片翻译、嵌字校对等需要在图片上建立文本标签的工作流。

核心目标：

- 兼容 LabelPlus 文本格式，降低旧项目迁移成本。
- 支持文件夹、图片、压缩包等常见图片来源。
- 提供标注、文本编辑、OCR、图校对比等一体化流程。
- 尽量减少手动解压、截图转存、重复查找图片等琐碎操作。

## 界面预览

> 截图可能不是最新界面，实际效果以当前版本为准。

<p>
  <img width="45%" alt="LabelMinus 主界面预览" src="https://github.com/user-attachments/assets/c3286989-9c48-4dba-8661-ae578e3a0234" />
  <img width="45%" alt="LabelMinus 图校界面预览" src="https://github.com/user-attachments/assets/fe54b8ae-2c6d-48fb-9b20-b638aeba9b8d" />
</p>

## 快速开始

1. 新建翻译：选择包含图片的文件夹或压缩包，创建新的翻译文档。
2. 打开翻译：导入已有 LabelPlus 文本，程序会加载对应图片和标注。
3. 图片标注：在图片上左键打点，右键删除标注；选中标签后编辑文本。
4. 保存翻译：使用“保存翻译”或“另存为”保存当前进度。
5. 导出文本：根据需要导出当前文本、原始文本或修改文档。
6. 图校对比：点击“图校”进入图片校对界面，对比两组图片并截图反馈。

## 功能特性

- LabelPlus 兼容：支持 LabelPlus 翻译文本的导入、编辑和导出。
- 图片来源灵活：支持文件夹、图片集合、zip / 7z / rar 压缩包预览与图校。
- 标注与文本编辑：支持打点、删除、拖动标签、快速选中标签并编辑文本。
- 标签样式设置：支持标签点样式、背景色、文字色、透明度和缩放设置。
- 深浅模式：主界面和图校界面均支持深色/浅色模式切换。
- 自动保存：可设置自动保存间隔，并可从设置窗口打开自动保存文件夹。
- 右键打开：可在设置中注册文件/文件夹右键菜单入口。
- 启动偏好：可设置下次启动时直接进入图校页面。

## OCR 功能

LabelMinus 提供多种 OCR 辅助方式，不同功能对环境要求不同：

- 网页字体识别：截图后打开识别网站，并把当前截图交给网页处理。
- 一键打点：使用 PaddleOCR 检测文字区域，并在图片上自动打点。
- 一键中英识别：使用 PaddleOCR v5 进行中英文识别与打点。
- 一键日文识别：使用 manga-ocr 进行日文检测、识别与打点。
- 截图 OCR：在截图模式下对局部截图进行识别，并自动新建标签。

部分 OCR 功能需要 Python、模型文件或相关运行环境。可在程序内通过“OCR -> 配置 OCR 环境”进行配置；如自动安装失败，可参考输出目录中的模型安装说明。

## 图校模式

图校模式用于对比两组图片，适合检查嵌字前后、不同版本或不同来源的图片差异。

- 支持直接打开文件夹、图片集合、zip / 7z / rar 压缩包。
- 支持左右两侧同步显示同名或相似文件名图片。
- 支持同步拖动和同步缩放，便于对齐细节。
- 支持截图到剪贴板，方便向嵌字者反馈。
- 支持截图后简单标记，右键可清除标记。
- 支持模糊文件名匹配。

## 设置与自动保存

设置入口位于顶部菜单“设置”。

当前设置项包括：

- 下次打开直接进入图校页面。
- 右键打开：注册或取消文件/文件夹右键菜单入口。
- 自动保存间隔：以分钟为单位设置自动保存频率。

设置文件保存在程序所在目录的 `settings.json` 中。自动保存文件保存在程序所在目录的 `AutoSave` 文件夹中。

## 快捷键

主窗口常用快捷键（未编辑文本时）：

| 快捷键 | 功能 |
| --- | --- |
| Ctrl+N | 为文件夹新建翻译 |
| Ctrl+Shift+N | 为压缩包新建翻译 |
| Ctrl+O | 导入翻译 |
| Alt+O | 预览压缩包 |
| Ctrl+Shift+O | 预览文件夹 |
| Ctrl+Alt+O | 选择图片 |
| Ctrl+S | 保存翻译 |
| Ctrl+Shift+S | 翻译另存为 |
| Ctrl+Z | 撤销 |
| Ctrl+Y | 重做 |
| A / D | 上一张 / 下一张 |
| R | 当前图片适应视图 |

图校模式常用快捷键：

| 快捷键 | 功能 |
| --- | --- |
| Q | 截图 |
| A / Left | 上一张 |
| D / Right | 下一张 |
| R | 重置视图 |
| Ctrl+R | 重置分割线 |
| G | 切换单图/双图校对 |
| C | 交换左右图片 |
| P | 清空图片 |
| H | 分割线跟随鼠标 |
| F1 | 打开/关闭菜单 |

## 开发与构建

技术栈：

- .NET 10 / `net10.0-windows`
- WPF
- MaterialDesignThemes / MaterialDesignColors
- CommunityToolkit.Mvvm
- Microsoft WebView2
- RapidOcrNet
- SharpCompress

## 将来想做的

- 持续修复 bug。
- 优化图校、OCR、标注和设置体验。
- 继续完善自动保存与工作区恢复能力。
- 计划增加可选的 AI 辅助功能。

## 致谢

- [LabelPlus](https://noodlefighter.com/label_plus/)：提供原始工作流与格式参考。
- [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR)：提供 OCR 相关能力。
- [manga-ocr](https://github.com/kha-white/manga-ocr)：提供日文 OCR 相关能力。
- [RapidOcrNet](https://github.com/RapidAI/RapidOCR)：提供 OCR 引擎封装。
- [MaterialDesignInXAML](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit)：提供 WPF Material Design 组件。
- [SharpCompress](https://github.com/adamhathcock/sharpcompress)：提供压缩包读取能力。
- [Microsoft WebView2](https://developer.microsoft.com/microsoft-edge/webview2/)：提供网页 OCR 相关的浏览器承载能力。

更多第三方组件与模型来源见 [ThirdPartyNotices.txt](ThirdPartyNotices.txt)。

## License

本项目使用 MIT License，详见 [LICENSE.txt](LICENSE.txt)。

如果本程序不慎使用了任何侵权内容，请及时联系处理。
