using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace WindowCapture;

class Program
{
    #region Win32 API

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const uint PW_RENDERFULLCONTENT = 2;

    #endregion

    private static readonly HttpClient httpClient = new();
    private const int DEFAULT_CDP_PORT = 9222;

    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        // Parse arguments
        bool listWindows = args.Contains("--list");
        bool listTabs = args.Contains("--tabs");
        bool exactMatch = args.Contains("--exact");
        bool isTab = args.Contains("--tab");
        bool fullPage = args.Contains("--scroll");
        int port = GetPortArg(args) ?? DEFAULT_CDP_PORT;

        // Get the search term (first non-flag argument)
        string? searchTerm = args.FirstOrDefault(a => !a.StartsWith("--"));

        if (listWindows)
        {
            ListWindows();
            return 0;
        }

        if (listTabs)
        {
            return await ListBrowserTabs(port);
        }

        if (searchTerm == null)
        {
            Console.Error.WriteLine("Error: No search term provided");
            PrintUsage();
            return 1;
        }

        if (isTab)
        {
            return await CaptureBrowserTab(searchTerm, exactMatch, port, fullPage);
        }
        else
        {
            return CaptureWindowByTitle(searchTerm, exactMatch);
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("WindowCapture - Capture windows and browser tabs without bringing them to focus");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  WindowCapture <title> [options]     Capture a window by title");
        Console.WriteLine("  WindowCapture --tab <title>         Capture a browser tab by title/URL");
        Console.WriteLine("  WindowCapture --list                List all visible windows");
        Console.WriteLine("  WindowCapture --tabs                List all browser tabs (requires CDP)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --exact          Match title exactly (default: partial match)");
        Console.WriteLine("  --tab            Capture browser tab instead of window (uses CDP)");
        Console.WriteLine("  --scroll         Capture full scrollable page as one tall image (tab only)");
        Console.WriteLine("  --port <num>     CDP port (default: 9222)");
        Console.WriteLine();
        Console.WriteLine("Browser Tab Capture Setup:");
        Console.WriteLine("  Edge:   Start with: msedge --remote-debugging-port=9222");
        Console.WriteLine("  Chrome: Start with: chrome --remote-debugging-port=9222");
        Console.WriteLine("  Or enable in edge://flags -> \"Enable remote debugging\"");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  WindowCapture --list");
        Console.WriteLine("  WindowCapture \"Notepad\"");
        Console.WriteLine("  WindowCapture --tabs");
        Console.WriteLine("  WindowCapture --tab \"GitHub\"");
        Console.WriteLine("  WindowCapture --tab \"GitHub\" --scroll");
        Console.WriteLine("  WindowCapture --tab \"google.com\" --port 9223");
    }

    private static int? GetPortArg(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--port" && int.TryParse(args[i + 1], out int port))
                return port;
        }
        return null;
    }

    #region Window Capture

    private static int CaptureWindowByTitle(string searchTitle, bool exactMatch)
    {
        var window = FindWindowByTitle(searchTitle, exactMatch);
        if (window == IntPtr.Zero)
        {
            Console.Error.WriteLine($"Error: No window found matching \"{searchTitle}\"");
            Console.Error.WriteLine("Use --list to see available windows");
            return 1;
        }

        string windowTitle = GetWindowTitle(window);
        string? screenshotPath = CaptureWindow(window);

        if (screenshotPath != null)
        {
            Console.WriteLine($"Screenshot saved: {screenshotPath}");
            Console.WriteLine($"Window: {windowTitle}");
            return 0;
        }
        else
        {
            Console.Error.WriteLine("Error: Failed to capture window");
            return 1;
        }
    }

    private static void ListWindows()
    {
        Console.WriteLine("Visible windows:");
        Console.WriteLine(new string('-', 80));

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            string title = GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title))
                return true;

            GetWindowRect(hWnd, out RECT rect);
            if (rect.Width <= 0 || rect.Height <= 0)
                return true;

            Console.WriteLine($"[{hWnd:X8}] {title}");
            Console.WriteLine($"           Size: {rect.Width}x{rect.Height}");

            return true;
        }, IntPtr.Zero);
    }

    private static IntPtr FindWindowByTitle(string searchTitle, bool exactMatch)
    {
        IntPtr foundWindow = IntPtr.Zero;
        string searchLower = searchTitle.ToLowerInvariant();

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            string title = GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title))
                return true;

            bool matches = exactMatch
                ? title.Equals(searchTitle, StringComparison.OrdinalIgnoreCase)
                : title.ToLowerInvariant().Contains(searchLower);

            if (matches)
            {
                foundWindow = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return foundWindow;
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        int length = GetWindowTextLength(hWnd);
        if (length == 0)
            return string.Empty;

        var sb = new StringBuilder(length + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string? CaptureWindow(IntPtr hWnd)
    {
        RECT rect;
        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) != 0)
        {
            GetWindowRect(hWnd, out rect);
        }

        int width = rect.Width;
        int height = rect.Height;

        if (width <= 0 || height <= 0)
        {
            Console.Error.WriteLine("Error: Window has invalid dimensions");
            return null;
        }

        IntPtr windowDC = GetWindowDC(hWnd);
        if (windowDC == IntPtr.Zero)
        {
            Console.Error.WriteLine("Error: Could not get window DC");
            return null;
        }

        try
        {
            IntPtr memDC = CreateCompatibleDC(windowDC);
            IntPtr hBitmap = CreateCompatibleBitmap(windowDC, width, height);
            IntPtr oldBitmap = SelectObject(memDC, hBitmap);

            bool success = PrintWindow(hWnd, memDC, PW_RENDERFULLCONTENT);

            if (!success)
            {
                success = PrintWindow(hWnd, memDC, 0);
            }

            SelectObject(memDC, oldBitmap);

            if (!success)
            {
                DeleteObject(hBitmap);
                DeleteDC(memDC);
                Console.Error.WriteLine("Error: PrintWindow failed");
                return null;
            }

            using var bitmap = Image.FromHbitmap(hBitmap);

            DeleteObject(hBitmap);
            DeleteDC(memDC);

            string fileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string filePath = Path.Combine(Path.GetTempPath(), fileName);

            bitmap.Save(filePath, ImageFormat.Png);

            return filePath;
        }
        finally
        {
            ReleaseDC(hWnd, windowDC);
        }
    }

    #endregion

    #region Browser Tab Capture (CDP)

    private class BrowserTab
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string Type { get; set; } = "";
        public string WebSocketDebuggerUrl { get; set; } = "";
    }

    private static async Task<List<BrowserTab>> GetBrowserTabs(int port)
    {
        try
        {
            string json = await httpClient.GetStringAsync($"http://localhost:{port}/json");
            var tabs = JsonSerializer.Deserialize<List<BrowserTab>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return tabs?.Where(t => t.Type == "page").ToList() ?? new List<BrowserTab>();
        }
        catch (HttpRequestException)
        {
            return new List<BrowserTab>();
        }
    }

    private static async Task<int> ListBrowserTabs(int port)
    {
        var tabs = await GetBrowserTabs(port);

        if (tabs.Count == 0)
        {
            Console.Error.WriteLine($"Error: No browser tabs found on port {port}");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Make sure your browser is running with remote debugging enabled:");
            Console.Error.WriteLine("  Edge:   msedge --remote-debugging-port=9222");
            Console.Error.WriteLine("  Chrome: chrome --remote-debugging-port=9222");
            return 1;
        }

        Console.WriteLine($"Browser tabs (CDP port {port}):");
        Console.WriteLine(new string('-', 80));

        foreach (var tab in tabs)
        {
            Console.WriteLine($"[{tab.Id[..8]}] {tab.Title}");
            Console.WriteLine($"           {tab.Url}");
        }

        return 0;
    }

    private static async Task<int> CaptureBrowserTab(string searchTerm, bool exactMatch, int port, bool fullPage)
    {
        var tabs = await GetBrowserTabs(port);

        if (tabs.Count == 0)
        {
            Console.Error.WriteLine($"Error: No browser tabs found on port {port}");
            Console.Error.WriteLine("Start browser with: msedge --remote-debugging-port=9222");
            return 1;
        }

        string searchLower = searchTerm.ToLowerInvariant();
        var matchingTab = tabs.FirstOrDefault(t =>
        {
            if (exactMatch)
            {
                return t.Title.Equals(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                       t.Url.Equals(searchTerm, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                return t.Title.ToLowerInvariant().Contains(searchLower) ||
                       t.Url.ToLowerInvariant().Contains(searchLower);
            }
        });

        if (matchingTab == null)
        {
            Console.Error.WriteLine($"Error: No tab found matching \"{searchTerm}\"");
            Console.Error.WriteLine("Use --tabs to see available tabs");
            return 1;
        }

        if (string.IsNullOrEmpty(matchingTab.WebSocketDebuggerUrl))
        {
            Console.Error.WriteLine("Error: Tab does not have WebSocket debugger URL");
            return 1;
        }

        string? screenshotPath = await CaptureTabViaCDP(matchingTab, fullPage);

        if (screenshotPath != null)
        {
            Console.WriteLine($"Screenshot saved: {screenshotPath}");
            Console.WriteLine($"Tab: {matchingTab.Title}");
            Console.WriteLine($"URL: {matchingTab.Url}");
            return 0;
        }
        else
        {
            Console.Error.WriteLine("Error: Failed to capture tab");
            return 1;
        }
    }

    // Sends a CDP command and waits for the response matching the given id,
    // discarding any event messages that arrive before it.
    private static async Task<JsonDocument> SendCdpCommandAsync(ClientWebSocket ws, int id, string method, object? cmdParams = null)
    {
        var command = new Dictionary<string, object?> { ["id"] = id, ["method"] = method };
        if (cmdParams != null)
            command["params"] = cmdParams;

        string commandJson = JsonSerializer.Serialize(command);
        await ws.SendAsync(Encoding.UTF8.GetBytes(commandJson), WebSocketMessageType.Text, true, CancellationToken.None);

        while (true)
        {
            using var ms = new MemoryStream();
            var chunk = new byte[65536];
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(chunk, CancellationToken.None);
                ms.Write(chunk, 0, result.Count);
            } while (!result.EndOfMessage);

            var doc = JsonDocument.Parse(ms.ToArray());
            if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.GetInt32() == id)
                return doc;

            doc.Dispose();
        }
    }

    private static async Task<string?> CaptureTabViaCDP(BrowserTab tab, bool fullPage)
    {
        using var ws = new ClientWebSocket();

        try
        {
            await ws.ConnectAsync(new Uri(tab.WebSocketDebuggerUrl), CancellationToken.None);

            string base64Data;

            if (fullPage)
            {
                // Step 1: get full page dimensions
                using var metricsDoc = await SendCdpCommandAsync(ws, 1, "Page.getLayoutMetrics");
                var resultProp = metricsDoc.RootElement.GetProperty("result");

                // Prefer cssContentSize (Chrome 77+), fall back to contentSize
                var contentSize = resultProp.TryGetProperty("cssContentSize", out var css)
                    ? css
                    : resultProp.GetProperty("contentSize");

                int pageWidth = (int)Math.Ceiling(contentSize.GetProperty("width").GetDouble());
                int pageHeight = (int)Math.Ceiling(contentSize.GetProperty("height").GetDouble());

                // Step 2: expand viewport to full page height
                using var overrideDoc = await SendCdpCommandAsync(ws, 2, "Emulation.setDeviceMetricsOverride", new
                {
                    width = pageWidth,
                    height = pageHeight,
                    deviceScaleFactor = 1,
                    mobile = false
                });

                // Step 3: capture full page
                using var screenshotDoc = await SendCdpCommandAsync(ws, 3, "Page.captureScreenshot", new
                {
                    format = "png",
                    captureBeyondViewport = true
                });

                // Step 4: restore original viewport
                using var clearDoc = await SendCdpCommandAsync(ws, 4, "Emulation.clearDeviceMetricsOverride");

                if (!screenshotDoc.RootElement.TryGetProperty("result", out var screenshotResult) ||
                    !screenshotResult.TryGetProperty("data", out var dataEl))
                {
                    if (screenshotDoc.RootElement.TryGetProperty("error", out var err))
                        Console.Error.WriteLine($"CDP Error: {err}");
                    return null;
                }

                base64Data = dataEl.GetString()!;
            }
            else
            {
                using var screenshotDoc = await SendCdpCommandAsync(ws, 1, "Page.captureScreenshot", new
                {
                    format = "png",
                    captureBeyondViewport = false
                });

                if (!screenshotDoc.RootElement.TryGetProperty("result", out var screenshotResult) ||
                    !screenshotResult.TryGetProperty("data", out var dataEl))
                {
                    if (screenshotDoc.RootElement.TryGetProperty("error", out var err))
                        Console.Error.WriteLine($"CDP Error: {err}");
                    return null;
                }

                base64Data = dataEl.GetString()!;
            }

            byte[] imageData = Convert.FromBase64String(base64Data);

            string fileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string filePath = Path.Combine(Path.GetTempPath(), fileName);

            await File.WriteAllBytesAsync(filePath, imageData);
            return filePath;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error capturing tab: {ex.Message}");
            return null;
        }
        finally
        {
            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
        }
    }

    #endregion
}
