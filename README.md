# FastBar

Hi there! 👋 FastBar is a lightweight launcher I built to quickly access my apps on Windows. When you press `Alt + Space`, a small search bar pops up near the top of the screen. Just start typing the name of an app, and FastBar will find and launch it instantly. If nothing matches, it redirects your query to a web search instead. Super simple.

## ✨ Features

* ⌖ Global shortcut (`Alt + Space`) for instant access anytime.
* 🗐 Live filtering as you type.
* 🌐 Falls back to web search (via DuckDuckGo) if no app is found.
* 💡 Clean, semi-transparent UI with soft shadows.
* ✨ Runs quietly in the background with minimal resource usage.

## 🚀 How It Works

1. On startup, it scans your Start Menu folders for `.lnk` shortcut files.
2. These are indexed by name and stored internally.
3. Pressing `Alt + Space` brings up the launcher window.
4. As you type, matching results are listed instantly.
5. Press `Enter` to either launch the selected app or perform a web search.

## 📦 Portable by Design

FastBar is completely portable — no installation required. Just run the `.exe` and you're good to go.

However, due to Windows security restrictions, **on first startup after boot, you need to click the `.exe` once manually** (e.g., from a shortcut or startup folder). After that, it will keep running quietly in the background and can always be triggered with `Alt + Space`.

## 🛠 Build Your Own (.exe)

If you want to generate a standalone `.exe` version yourself:

```bash
pyinstaller --onefile --noconsole --icon=fastbaricon.ico --name=FastBar fastbar.py
```

The compiled `.exe` will be available under the `dist/` folder. You can share it or add it to your Startup folder for auto-run.

## 💻 For Developers

If you want to run the code manually:

1. Install dependencies:

   ```bash
   pip install pyqt5
   ```
2. Launch the script:

   ```bash
   python fastbar.py
   ```
3. Hit `Alt + Space` to toggle the UI.

## ⚠ Notes

* Currently works only on Windows.
* Some systems might already use `Alt + Space` (e.g., accessibility tools). In that case, you may need to free that key combo first.

## 📁 Contributing

Suggestions, issues, or pull requests are very welcome. Ideas like file search, custom themes, alternate search engines — I'd love to see what you come up with.

---

I built FastBar for my own daily use, but if it helps you too, that makes me genuinely happy ✨
