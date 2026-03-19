# 带标签截图功能设计

**日期**: 2026-03-15
**功能**: 带标签截图

## 1. 功能概述

在主界面底部工具栏添加"带标签截图"按钮，点击后：
1. 截取当前图片 + 可见标签 + 下方文字
2. 复制到剪贴板
3. 保存到 `ScreenShottemp` 文件夹

## 2. 需求确认

| 需求项 | 详情 |
|--------|------|
| 截图范围 | 图片 + ImageLabelViewer 上显示的所有 LabelNode 标签 |
| 标签显示（图片上） | ImageLabelViewer 原有的标签节点渲染 |
| 标签显示（图片下方） | 格式：`[1]:文字内容`，每行一个标签 |
| 包含标签 | 仅包含未删除的标签（IsDeleted = false） |
| 不包含 | 标签所属组别 |
| 输出方式 | 同时：剪贴板复制 + 保存到文件 |
| 保存位置 | `ScreenShottemp` 文件夹 |
| 文件格式 | JPG |
| 按钮位置 | 底部工具栏，"图校"按钮旁边 |
| 快捷键 | 无 |

## 3. UI 设计

### 按钮位置
```
[适应视图] [适应宽度] [适应高度] [横向] [纵向] | [修改图片集] [图片选择器] [↑] [↓] | [文件夹] [图校] [截图📷]
```

### 截图效果预览
```
┌─────────────────────────┐
│     图片 + 标签节点      │  ← ImageLabelViewer 显示内容
│                         │
├─────────────────────────┤
│   [1]:abcd              │  ← 底部标签文字
│   [2]:efgh             │
└─────────────────────────┘
```

## 4. 技术实现

### 4.1 修改文件

| 文件 | 修改内容 |
|------|----------|
| `SelfControls/ImageLabelViewer.xaml.cs` | 新增 `CaptureWithLabels()` 方法，渲染整个 ViewportGrid 到 BitmapSource |
| `MainVM.cs` | 新增 `CaptureWithLabelsCommand` 命令 |
| `MainWindow.xaml` | 底部工具栏添加截图按钮 |

### 4.2 关键技术点

1. **捕获图片+标签**
   - 使用 `RenderTargetBitmap` 渲染整个 ViewportGrid（包含 TargetImage 和所有 LabelNode）
   - 复用 ImageLabelViewer 现有的可视化渲染逻辑

2. **生成底部文字**
   - 从 `SelectedImage.ActiveLabels` 获取未删除标签
   - 格式：`[$index]:$text`，每行一个
   - 参考 CompareImgControl 的页脚渲染逻辑

3. **保存逻辑**
   - 复用 CompareImgControl 的 `CombineAndSave` 逻辑
   - 复用 `SaveInBackground` 方法保存到文件
   - 使用 `Clipboard.SetImage` 复制到剪贴板

### 4.3 关键方法

```csharp
// ImageLabelViewer.xaml.cs
public BitmapSource? CaptureWithLabels()
{
    // 渲染整个 ViewportGrid 到 BitmapSource
    var rtb = new RenderTargetBitmap(...);
    rtb.Render(ViewportGrid);
    return rtb;
}

// MainVM.cs
[RelayCommand]
private void CaptureWithLabels()
{
    var imageWithLabels = _picView.CaptureWithLabels();
    var labelsText = BuildLabelsText(SelectedImage.ActiveLabels);
    CombineAndSave(imageWithLabels, labelsText);
}
```

## 5. 测试用例

| 测试项 | 预期结果 |
|--------|----------|
| 按钮显示 | 底部工具栏显示截图按钮 |
| 正常截图 | 点击按钮后截图保存到文件 + 复制到剪贴板 |
| 无标签 | 截图正常，仅有图片 |
| 有标签 | 图片下方正确显示 `[1]:文字` 格式 |
| 已删除标签 | 不显示在截图底部文字中 |
| 空标签文字 | 显示 `[1]:` |

## 6. 风险与注意事项

1. **性能**：大图片渲染可能较慢，考虑异步处理
2. **布局**：确保 ViewportGrid 渲染时尺寸正确
3. **兼容性**：复用现有截图保存逻辑，保持行为一致
