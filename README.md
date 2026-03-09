# WindowCapture

[![NuGet](https://img.shields.io/nuget/v/WindowCapture.svg)](https://www.nuget.org/packages/WindowCapture)
[![NuGet Downloads](https://img.shields.io/nuget/dt/WindowCapture.svg)](https://www.nuget.org/packages/WindowCapture)
[![Build](https://github.com/Hymma/WindowCapture/actions/workflows/build.yml/badge.svg)](https://github.com/Hymma/WindowCapture/actions/workflows/build.yml)

A .NET global tool that captures screenshots of windows and browser tabs **without bringing them to focus**. Uses Win32 `PrintWindow` API for windows and Chrome DevTools Protocol (CDP) for browser tabs.

## Install

```bash
dotnet tool install --global WindowCapture
```

Requires .NET 8+ on Windows.

## Usage

### Window Capture

```bash
# List all visible windows with their handle and size
windowcapture --list

# Capture a window by partial title match (case-insensitive)
windowcapture "Notepad"

# Capture a window by exact title
windowcapture "Untitled - Notepad" --exact

# Capture Visual Studio, Outlook, etc.
windowcapture "Visual Studio"
windowcapture "Inbox"
windowcapture "Teams"
```

### Browser Tab Capture

```bash
# List all open browser tabs
windowcapture --tabs

# Capture a tab by title
windowcapture --tab "GitHub"
windowcapture --tab "Stack Overflow"

# Capture a tab by URL
windowcapture --tab "google.com"
windowcapture --tab "localhost:3000"

# Use a custom CDP port (default: 9222)
windowcapture --tabs --port 9223
windowcapture --tab "GitHub" --port 9223
```

### Output

Screenshots are saved as PNG files in the system temp directory:

```
Screenshot saved: C:\Users\You\AppData\Local\Temp\screenshot_20260310_094405.png
Window: Notepad - Untitled
```

## Browser Tab Setup

To capture browser tabs, the browser must be running with remote debugging enabled:

```bash
# Edge
msedge --remote-debugging-port=9222

# Chrome
chrome --remote-debugging-port=9222
```

Or enable permanently in `edge://flags` → "Enable remote debugging".

## Features

- **Silent capture** — does not bring the window to the foreground or change focus
- **Background windows** — captures windows hidden behind others
- **Browser tabs** — captures any tab, not just the active one (via CDP)
- **Partial matching** — default search is case-insensitive substring match
- **No dependencies** — single dotnet tool, no native binaries to install

## Options

| Option | Description |
|--------|-------------|
| `--list` | List all visible windows |
| `--tabs` | List all browser tabs (requires CDP) |
| `--tab` | Capture a browser tab instead of a window |
| `--exact` | Match title exactly instead of partial match |
| `--port <num>` | CDP port (default: 9222) |

## License

MIT
