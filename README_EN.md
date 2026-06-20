<p align="center">
  <img src="Assets/app_icon.ico" width="64"/>
  <br><a href="README.md">中文</a> | English
  <br>Global hotkey screenshot · built-in annotation editor
  <br>A lightweight Windows screenshot tool
</p>
<p align="center">
  <a href="#"><img src="https://img.shields.io/badge/version-1.0.0-blue.svg?style=popout-square" alt="Version"></a>
  <a href="#"><img src="https://img.shields.io/badge/.NET-8.0-512BD4.svg?style=popout-square" alt=".NET"></a>
  <a href="#"><img src="https://img.shields.io/badge/platform-Windows-brightgreen.svg?style=popout-square" alt="Platform"></a>
</p>
---
## Introduction
**Screenshot Spirit** is a Windows screenshot utility that supports full-screen snap, window snap, and custom drag-to-select capture. It includes a built-in annotation editor with rectangle, circle, line, curve, arrow, mosaic brush, and text tools.
Press a hotkey to start capturing, confirm your selection to enter the editor, then save or copy to clipboard in one smooth workflow.
## Features
| Feature | Description |
|---------|-------------|
| Multiple capture modes | Full-screen snap, window snap, drag-to-select custom region |
| 7 annotation tools | Rectangle, circle, line, curve, arrow, mosaic brush, text |
| Color & size | Pick any color, adjustable stroke width, per-tool settings |
| Mosaic brush | Brush-based mosaic with adjustable grain size |
| Undo / Redo | Full undo stack for all drawing operations |
| Zoom / Pan | Scroll to zoom, hold Space to pan |
| Save / Copy | Export to PNG or copy to clipboard |
## Quick Start
### Prerequisites
- Windows 10 / 11
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
### Build and Run
```
git clone https://github.com/Nimeld/Screenshot-Spirit.git
cd Screenshot-Spirit
dotnet build -c Release
start ScreenshotSpirit/bin/Release/net8.0-windows/ScreenshotSpirit.exe
```
### Usage
1. Press the global hotkey to start capture
2. Left-click drag to draw a selection region, or single-click a window for window snap
3. Press Enter to confirm or Esc to cancel the selection
4. Annotate with the toolbar in the editor
5. Ctrl+S to save, Ctrl+C to copy, Esc to close
## Project Structure
```
ScreenshotSpirit/
  Models/          # Data models
  Services/        # Business logic
  Views/           # WPF windows and controls
  Assets/          # Icons and resources
  ScreenshotSpirit.csproj
```
## License
MIT
