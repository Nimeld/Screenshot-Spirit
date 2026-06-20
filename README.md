<p align="center">
  <img src="Assets/app_icon.ico" width="128" />
  <br />中文 | <a href="README_EN.md">English</a>
  <br />全局热键截图 · 内置标注编辑器
  <br />轻量级 Windows 截图精灵
</p>

<p align="center">
  <a href="#"><img src="https://img.shields.io/badge/版本-1.0.0-blue.svg?style=flat-square" alt="版本" /></a>
  <a href="#"><img src="https://img.shields.io/badge/.NET-8.0-512BD4.svg?style=flat-square" alt=".NET" /></a>
  <a href="#"><img src="https://img.shields.io/badge/平台-Windows-brightgreen.svg?style=flat-square" alt="平台" /></a>
</p>

---

## 简介

**Screenshot Spirit（截图精灵）** 是一款 Windows 截图工具，支持全屏 / 窗口吸附 / 自定义选区截图，并内置标注编辑器。

可对截图进行矩形、圆形、直线、曲线、箭头、马赛克笔刷、文字等 7 种标注。按下快捷键截图，选区完成后进入编辑界面，保存或复制到剪贴板一气呵成。

## 功能

| 功能 | 说明 |
| --- | --- |
| 多种截图模式 | 全屏吸附、窗口吸附、拖拽自定义选区 |
| 七种标注工具 | 矩形、圆形、直线、曲线、箭头、马赛克笔刷、文字 |
| 取色与粗细 | 任意取色，粗细可调，每个工具独立参数 |
| 马赛克笔刷 | 笔刷式涂抹，颗粒大小可调 |
| 撤销 / 重做 | 完整撤销栈，支持所有绘制操作 |
| 缩放 / 平移 | 滚轮缩放，按住空格拖拽平移画布 |
| 保存 / 复制 | 导出 PNG 或复制到剪贴板 |

## 快速开始

### 环境要求

- Windows 10 / 11
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

### 构建与运行

```bash
git clone https://github.com/Nimeld/Screenshot-Spirit.git
cd Screenshot-Spirit
dotnet build -c Release
```

输出：`bin/Release/net8.0-windows/ScreenshotSpirit.exe`

### 使用方式

1. 按下全局热键启动截图
2. 左键拖拽绘制选区，或单击窗口进行窗口吸附
3. 选区待确认状态下，按 `Enter` 确认或 `Esc` 退出
4. 进入编辑界面后，使用工具栏进行标注
5. `Ctrl+S` 保存，`Ctrl+C` 复制，`Esc` 关闭

## 项目结构

```
App.xaml                 # 应用入口
MainWindow.xaml          # 主窗口（托盘）
Views/
  CaptureWindow.xaml     # 截图选区窗口
  EditWindow.xaml        # 标注编辑窗口
  SettingsWindow.xaml    # 设置窗口
Models/
  AppSettings.cs         # 配置模型
Services/
  CaptureService.cs      # 截图服务
  HotkeyService.cs       # 全局热键
  SettingsService.cs     # 配置读写
  AutoStartService.cs    # 开机自启
Assets/
  app_icon.ico           # 应用图标
ScreenshotSpirit.csproj  # 项目文件
```

## 许可证

MIT
