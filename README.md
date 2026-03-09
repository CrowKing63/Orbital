# Orbital

**A free, open-source alternative to Snipdo for Windows.**

Orbital is a lightweight Windows tray app that pops up an AI-powered action bar whenever you select text — letting you translate, summarize, rewrite, search, or run any custom prompt in one click, right where you're working.

---

## Why Orbital?

[Snipdo](https://snipdo-app.com/) pioneered the idea of a floating toolbar on text selection, but it's **subscription-only**. Orbital is the free, open-source answer:

| | Orbital | Snipdo |
|---|---|---|
| Price | **Free** | Subscription |
| Source | **Open source** (MIT) | Closed |
| AI provider | **Any OpenAI-compatible API** | Proprietary |
| Custom prompts | **Unlimited** | Limited |
| Local models | **Yes** (Ollama, LM Studio) | No |
| Platform | Windows 10/11 | Windows |

---

## Features

- **Instant action bar** — select any text anywhere on Windows, get a floating pill menu above your cursor
- **AI actions** — Replace, Copy to clipboard, or Popup with AI-generated results
- **Utility actions** — Copy, Cut, Paste, and Google Search without any AI calls
- **Bring your own model** — works with OpenAI, OpenRouter (including free models), Ollama, LM Studio, or any OpenAI-compatible endpoint
- **Custom prompts** — add, edit, and reorder actions with your own prompt templates using `{text}` as a placeholder
- **Long-press trigger** — hold the mouse button to open the menu without selecting text (great for Paste)
- **Secure key storage** — API keys are encrypted with Windows DPAPI, stored locally

---

## Demo

> Select text → action bar appears above cursor → click an action

*(screenshot / GIF coming soon)*

---

## Getting Started

### Requirements

- Windows 10 or 11 (x64)
- .NET 8 Runtime ([download](https://dotnet.microsoft.com/download/dotnet/8.0))

### Download

Grab the latest release from the [Releases](../../releases) page.

- **Installer**: Run `Orbital-Setup.exe` — installs to Program Files with an uninstaller
- **Portable**: Extract the zip and run `Orbital.exe` directly — no installation required

### First-time Setup

1. Run `Orbital.exe` — a tray icon appears in the bottom-right corner
2. Double-click the tray icon (or right-click → **Settings**)
3. Choose your AI provider and enter your API key
4. Start selecting text anywhere on your desktop

#### Free option — no credit card required

Select **OpenRouter** as the provider and use `openrouter/free` as the model name. OpenRouter automatically routes to available free models at no cost.

---

## Configuration

### Providers

| Provider | Base URL | Notes |
|---|---|---|
| OpenAI | `https://api.openai.com/v1` | Requires paid key |
| OpenRouter | `https://openrouter.ai/api/v1` | Free tier available |
| Ollama (local) | `http://localhost:11434/v1` | No API key needed |
| LM Studio | `http://localhost:1234/v1` | No API key needed |
| Any other | custom URL | Must be OpenAI-compatible |

### Action Types

| Output Mode | Description |
|---|---|
| `Replace` | AI result overwrites your selected text (Ctrl+V) |
| `Copy` | AI result is copied to clipboard |
| `Popup` | AI result shown in a floating window (20s auto-close) |
| `DirectCopy` | Copies selected text as-is — no AI |
| `Cut` | Cuts selected text — no AI |
| `Paste` | Pastes clipboard at cursor — no AI |
| `Browser` | Opens Google Search with selected text — no AI |

### Custom Prompt Example

```
Name: Fix English
Prompt: Correct grammar and make the following text sound natural and professional: {text}
Output: Replace
```

---

## Building from Source

```bash
git clone https://github.com/CrowKing63/Orbital.git
cd Orbital
"C:\Program Files\dotnet\dotnet.exe" build Orbital.csproj
./bin/Debug/net8.0-windows/Orbital.exe
```

**Requirements:** .NET 8 SDK, Windows

The project uses WPF for UI and WinForms only for the `NotifyIcon` tray integration.

---

## Contributing

Pull requests are welcome. For major changes, please open an issue first.

Areas where contributions are especially appreciated:

- **More default actions** — practical prompt templates for common tasks
- **UI improvements** — accessibility, animations, DPI edge cases
- **Localization** — translations for the Settings UI
- **Packaging** — MSIX installer, winget manifest, GitHub Actions release pipeline

---

## License

[MIT](LICENSE)

---

## Acknowledgements

Inspired by [Snipdo](https://snipdo-app.com/), which showed that a floating text-action toolbar on Windows is genuinely useful — and showed that the market needs a free, open alternative.
