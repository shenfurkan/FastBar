import sys
import os
import webbrowser
import ctypes
from ctypes import wintypes
from threading import Thread

from PyQt5.QtWidgets import (
    QApplication, QWidget, QLineEdit, QListWidget, QVBoxLayout, QStyle,
    QGraphicsDropShadowEffect
)
from PyQt5.QtGui import QColor
from PyQt5.QtCore import Qt, QTimer, pyqtSignal, QObject

# Register global hotkey (Alt + Space)
MOD_ALT = 0x0001
VK_SPACE = 0x20
HOTKEY_ID = 1
user32 = ctypes.windll.user32

# Create a QObject to emit signal on hotkey press
class HotkeyListener(QObject):
    hotkeyPressed = pyqtSignal()

    def start(self):
        # Start the hotkey listener thread
        Thread(target=self._listen, daemon=True).start()

    def _listen(self):
        # Register Alt+Space as the system-wide hotkey
        if not user32.RegisterHotKey(None, HOTKEY_ID, MOD_ALT, VK_SPACE):
            print("Hotkey registration failed")
            return
        msg = wintypes.MSG()
        while user32.GetMessageW(ctypes.byref(msg), None, 0, 0) != 0:
            if msg.message == 0x0312 and msg.wParam == HOTKEY_ID:
                self.hotkeyPressed.emit()
            user32.TranslateMessage(ctypes.byref(msg))
            user32.DispatchMessageW(ctypes.byref(msg))

# Main widget class
class FastBar(QWidget):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("Fastbar")
        self.setWindowFlags(Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint)
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.shortcuts = {}

        # Initialize UI and shortcut indexing
        self.initUI()
        QTimer.singleShot(0, self.build_index)

        # Start global hotkey handler
        self.hotkey = HotkeyListener()
        self.hotkey.hotkeyPressed.connect(self.toggle_visibility)
        self.hotkey.start()

    def initUI(self):
        # Layout and visual styling
        layout = QVBoxLayout()
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(5)

        # Search input setup
        self.input = QLineEdit(self)
        self.input.setPlaceholderText('Fastbar Search')
        self.input.setFixedHeight(60)
        close_icon = self.style().standardIcon(QStyle.SP_TitleBarCloseButton)
        close_action = self.input.addAction(close_icon, QLineEdit.TrailingPosition)
        close_action.triggered.connect(self.hide)
        self.input.textChanged.connect(self.on_text_changed)
        self.input.returnPressed.connect(self.on_return)
        self.input.setStyleSheet('''
            QLineEdit {
                background: rgba(255, 255, 255, 200);
                border-radius: 12px;
                padding: 16px;
                padding-right: 40px;
                font-size: 18px;
            }
        ''')
        shadow = QGraphicsDropShadowEffect(self)
        shadow.setBlurRadius(15)
        shadow.setOffset(0)
        shadow.setColor(QColor(0, 0, 0, 100))
        self.input.setGraphicsEffect(shadow)

        # Search result list
        self.list_widget = QListWidget(self)
        self.list_widget.setStyleSheet('''
            QListWidget {
                background: rgba(255, 255, 255, 200);
                border-radius: 10px;
                font-size: 14px;
                padding: 5px;
            }
            QListWidget::item:selected {
                background: rgba(100, 100, 255, 150);
            }
        ''')
        list_shadow = QGraphicsDropShadowEffect(self)
        list_shadow.setBlurRadius(15)
        list_shadow.setOffset(0)
        list_shadow.setColor(QColor(0, 0, 0, 100))
        self.list_widget.setGraphicsEffect(list_shadow)

        self.list_widget.hide()
        self.list_widget.itemActivated.connect(self.on_return)

        # Add widgets to layout
        layout.addWidget(self.input)
        layout.addWidget(self.list_widget)
        self.setLayout(layout)

        # Size and position
        self.setFixedWidth(1000)
        self._min_height = (
            self.input.height() +
            layout.contentsMargins().top() + layout.contentsMargins().bottom()
        )
        self.setFixedHeight(self._min_height)
        self.center()

    def center(self):
        # Center the window on screen
        screen = QApplication.primaryScreen().availableGeometry()
        x = (screen.width() - self.width()) // 2
        y = screen.height() // 4
        self.move(x, y)

    def build_index(self):
        # Build dictionary of Start Menu shortcuts (.lnk files)
        bases = filter(None, (os.getenv('APPDATA'), os.getenv('PROGRAMDATA')))
        for base in bases:
            sm = os.path.join(base, 'Microsoft', 'Windows', 'Start Menu', 'Programs')
            for root, _, files in os.walk(sm):
                for f in files:
                    if f.lower().endswith('.lnk'):
                        name = f[:-4]
                        self.shortcuts[name] = os.path.join(root, f)

    def toggle_visibility(self):
        # Show or hide the window on hotkey press
        if self.isVisible():
            self.hide()
        else:
            self.center()
            self.show()
            self.raise_()
            self.activateWindow()
            self.input.setFocus()

    def on_text_changed(self, text):
        # Update search results as user types
        self.list_widget.clear()
        if not text or not self.shortcuts:
            self.list_widget.hide()
            self.setFixedHeight(self._min_height)
            return
        matches = [name for name in self.shortcuts if text.lower() in name.lower()]
        items = sorted(matches)[:7] if matches else [f"Search web for '{text}'"]
        self.list_widget.addItems(items)
        self.list_widget.setCurrentRow(0)
        self.list_widget.show()
        item_h = self.list_widget.sizeHintForRow(0)
        total_h = (
            self._min_height +
            item_h * self.list_widget.count() +
            self.layout().spacing()
        )
        self.setFixedHeight(total_h)

    def on_return(self):
        # Handle launching apps or web searches
        current = self.list_widget.currentItem()
        query = self.input.text().strip()

        # Direct match
        if query in self.shortcuts:
            os.startfile(self.shortcuts[query])
        elif current:
            text = current.text()
            if text.startswith("Search web for '"):
                q = text.split("'")[1]
                webbrowser.open(f"https://duckduckgo.com/?q={q}")
            else:
                path = self.shortcuts.get(text)
                if path:
                    os.startfile(path)
        else:
            # Fallback: try running input as path or command
            try:
                os.startfile(query)
            except Exception:
                pass

        # Reset UI state
        self.input.clear()
        self.list_widget.hide()
        self.setFixedHeight(self._min_height)
        self.hide()

if __name__ == '__main__':
    app = QApplication(sys.argv)
    fastbar = FastBar()
    fastbar.hide()
    sys.exit(app.exec_())
