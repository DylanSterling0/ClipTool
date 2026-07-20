# ClipTool

A Windows system-tray utility that monitors clipboard history and provides screenshot OCR (Optical Character Recognition) with one-click paste.

## Features

- **📋 Clipboard History** — Automatically records everything you copy. Search, paste, or delete items from the history window.
- **📷 Screenshot OCR** — Capture any screen region with a hotkey, extract text using Tesseract OCR (English + Chinese), and paste the result directly.
- **🔁 Auto Start on Boot** — Toggle via the tray menu to launch automatically on login.

## Download

Grab the latest release from [Releases](https://github.com/DylanSterling0/ClipTool/releases), or build from source.

### Prerequisites

You need the **.NET 7.0 Desktop Runtime**:

> [Download .NET 7.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/7.0)

On first launch, the app downloads ~113 MB of OCR language data (`tessdata_best` for English + Simplified Chinese). An internet connection is required.

## Usage

1. Run **ClipTool.exe**. It lives in the system tray (bottom-right).
2. The tray icon shows a balloon tip with your registered hotkeys.

### Hotkeys (auto-assigned)

| Action | Default Hotkey |
|--------|---------------|
| Show Clipboard History | `Win+Shift+<first available letter>` |
| Screenshot OCR | `Win+Shift+<second available letter>` |

The app scans for unregistered hotkey combinations on startup and assigns the first two available `Win+Shift+Letter` combos.

### Tray Menu

Right-click the tray icon to access:

| Menu Item | Action |
|-----------|--------|
| 📋 Clipboard History | Open the history window (also bound to hotkey) |
| 📷 Screenshot OCR | Start screenshot capture (also bound to hotkey) |
| 🔁 Auto Start on Boot | Toggle launch-at-login (checked = enabled) |
| 🧹 Clear History | Delete all clipboard history |
| ❌ Exit | Quit ClipTool |

### Screenshot OCR

1. Press the OCR hotkey or click "Screenshot OCR" in the tray menu.
2. The screen dims with a crosshair cursor.
3. **Drag** to select the region containing text.
4. **Release** — the selection is cropped and OCR begins.
5. The recognized text is automatically copied to your clipboard and pasted into your active window.

To cancel: press **ESC** or **right-click** at any time (during selection or recognition).

### Clipboard History

- **Double-click** an item to copy and paste it.
- **Ctrl+F** to focus the search bar.
- **Delete key** to remove the selected item.
- **Right-click** on an item for a context menu.

## Build from Source

```bash
git clone https://github.com/DylanSterling0/ClipTool.git
cd ClipTool
dotnet build
```

Requires the [.NET 7.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/7.0).

## Tech Stack

- **Language:** C# (.NET 7, Windows Forms)
- **OCR Engine:** [Tesseract](https://github.com/tesseract-ocr/tesseract) via [Tesseract.NET](https://github.com/charlesw/tesseract) (v5.2.0)
- **Language Data:** `tessdata_best` (English + Simplified Chinese)

## License

MIT
