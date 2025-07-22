# FastBar: A Spotlight Alternative for Windows
<img width="1178" height="357" alt="Screenshot 2025-07-22 054914" src="https://github.com/user-attachments/assets/2f98cd1e-14d4-4472-aee3-e6a057c0465c" />



FastBar is a simple app launcher for Windows — kind of like a minimalist Spotlight alternative. Just press `Alt + Space`, type a few letters, and launch anything instantly.

If it’s in your Start Menu, FastBar will find it. If not, it sends your search to the web. That’s it. No bloat, no distractions.

[⬇️ Download FastBar.exe](https://github.com/shenfurkan/FastBar/releases/download/1.0/FastBar.exe)

## ✨ What It Does

* ⌖ Pops up with `Alt + Space`
* 🗐 Live search through your Start Menu shortcuts
* 🌐 Sends unrecognized queries to DuckDuckGo
* 💡 Lightweight, transparent UI that stays out of the way
* ✨ Runs quietly in the background — no tray icons, no fuss

## ⚙ How It Works

1. On startup, FastBar indexes `.lnk` shortcut files from your Start Menu.
2. You press `Alt + Space`, and a search bar fades in.
3. You type — it filters live.
4. Hit `Enter` to run the app or search online.

## 📦 Portable By Nature

FastBar is 100% portable. Just run the `.exe`. No setup, no registry edits.

👉 Heads-up: due to Windows restrictions, **after reboot you need to run it manually once** (like from Startup folder). After that, it's ready any time you press `Alt + Space`.

## 🛠 Want to Build It Yourself?

```bash
pyinstaller --onefile --noconsole --icon=fastbaricon.ico --name=FastBar fastbar.py
```

The `.exe` will be in `dist/`. You can share it, add it to autostart, or keep it on a flash drive.

## 🧑‍💻 Dev Mode

Want to run from source?

```bash
pip install pyqt5
python fastbar.py
```

## ⚠ Heads Up

* Windows only for now
* If another app uses `Alt + Space`, you might need to rebind or disable that shortcut

## 🤝 Contribute

Pull requests, ideas, and improvements are always welcome. Themes, fuzzy search, multi-language — sky’s the limit.

---

FastBar started as a little tool I built for myself. If it saves you a few clicks too, that’s already a win. 🚀
