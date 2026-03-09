# WindowCapture

A .NET global tool that captures screenshots of windows and browser tabs **without bringing them to focus**. Uses Win32 `PrintWindow` API for windows and Chrome DevTools Protocol (CDP) for browser tabs.

## Install

```bash
dotnet tool install --global WindowCapture
```

Requires .NET 8+ on Windows.

## Usage

```bash
# List all visible windows
windowcapture --list

# Capture a window by partial title match
windowcapture "Notepad"

# Capture a window by exact title
windowcapture "Visual Studio Code" --exact

# List all browser tabs (requires CDP)
windowcapture --tabs

# Capture a browser tab by title or URL
windowcapture --tab "GitHub"
windowcapture --tab "google.com"

# Use custom CDP port
windowcapture --tabs --port 9223
```

Screenshots are saved as PNG files in the system temp directory.

## Browser Tab Capture

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

## License

MIT
