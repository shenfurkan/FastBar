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

# Set up Alt+Space as a global hotkey
MOD_ALT = 0x0001
VK_SPACE = 0x20
HOTKEY_ID = 1
user32 = ctypes.windll.user32

# Simple class to listen for hotkey presses
class HotkeyListener(QObject):
    hotkeyPressed = pyqtSignal()

    def start(self):
        # Run the listener in a background thread
        Thread(target=self._listen, daemon=True).start()

    def _listen(self):
        if not user32.RegisterHotKey(None, HOTKEY_ID, MOD_ALT, VK_SPACE):
            print("Failed to register hotkey")
            return
        msg = wintypes.MSG()
        while user32.GetMessageW(ctypes.byref(msg), None, 0, 0) != 0:
            if msg.message == 0x0312 and msg.wParam == HOTKEY_ID:
                self.hotkeyPressed.emit()
            user32.TranslateMessage(ctypes.byref(msg))
            user32.DispatchMessageW(ctypes.byref(msg))

class FastBar(QWidget):
    def __init__(self):
        super().__init__()

        # Basic window setup
        self.setWindowTitle("Fastbar")
        self.setWindowFlags(Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint)
        self.setAttribute(Qt.WA_TranslucentBackground)

        # Where we keep shortcut name → path mappings
        self.shortcuts = {}

        # Build UI and index app shortcuts
        self.initUI()
        QTimer.singleShot(0, self.build_index)

        # Listen for Alt+Space and toggle visibility
        self.hotkey = HotkeyListener()
        self.hotkey.hotkeyPressed.connect(self.toggle_visibility)
        self.hotkey.start()

    def initUI(self):
        layout = QVBoxLayout()
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(5)

        # Text input bar where you type your query
        self.input = QLineEdit(self)
        self.input.setPlaceholderText('Fastbar Search')
        self.input.setFixedHeight(60)

        # Add a close button inside the input field
        close_icon = self.style().standardIcon(QStyle.SP_TitleBarCloseButton)
        close_action = self.input.addAction(close_icon, QLineEdit.TrailingPosition)
        close_action.triggered.connect(self.hide)

        # Connect input events
        self.input.textChanged.connect(self.on_text_changed)
        self.input.returnPressed.connect(self.on_return)

        # Styling the input
        self.input.setStyleSheet('''
            QLineEdit {
                background: rgba(255, 255, 255, 200);
                border-radius: 12px;
                padding: 16px;
                padding-right: 40px;
                font-size: 18px;
            }
        ''')

        # Add a shadow to the input field
        shadow = QGraphicsDropShadowEffect(self)
        shadow.setBlurRadius(15)
        shadow.setOffset(0)
        shadow.setColor(QColor(0, 0, 0, 100))
        self.input.setGraphicsEffect(shadow)

        # Result list for matching shortcuts or search
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

        # Shadow for the list too
        list_shadow = QGraphicsDropShadowEffect(self)
        list_shadow.setBlurRadius(15)
        list_shadow.setOffset(0)
        list_shadow.setColor(QColor(0, 0, 0, 100))
        self.list_widget.setGraphicsEffect(list_shadow)

        self.list_widget.hide()
        self.list_widget.itemActivated.connect(self.on_return)

        # Put everything together
        layout.addWidget(self.input)
        layout.addWidget(self.list_widget)
        self.setLayout(layout)

        # Set window size
        self.setFixedWidth(1000)
        self._min_height = (
            self.input.height() +
            layout.contentsMargins().top() + layout.contentsMargins().bottom()
        )
        self.setFixedHeight(self._min_height)
        self.center()

    # Put the window in the center of the screen
    def center(self):
        screen = QApplication.primaryScreen().availableGeometry()
        x = (screen.width() - self.width()) // 2
        y = screen.height() // 4
        self.move(x, y)

    # Scan the Start Menu folders for app shortcuts (.lnk)
    def build_index(self):
        bases = filter(None, (os.getenv('APPDATA'), os.getenv('PROGRAMDATA')))
        for base in bases:
            sm = os.path.join(base, 'Microsoft', 'Windows', 'Start Menu', 'Programs')
            for root, _, files in os.walk(sm):
                for f in files:
                    if f.lower().endswith('.lnk'):
                        name = f[:-4]
                        self.shortcuts[name] = os.path.join(root, f)

    # Show or hide the FastBar on hotkey press
    def toggle_visibility(self):
        if self.isVisible():
            self.hide()
        else:
            self.center()
            self.show()
            self.raise_()
            self.activateWindow()
            self.input.setFocus()

    # Update result list as user types
    def on_text_changed(self, text):
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

    # Handle what happens when you press Enter
    def on_return(self):
        current = self.list_widget.currentItem()
        query = self.input.text().strip()

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
            try:
                os.startfile(query)
            except Exception:
                pass

        self.input.clear()
        self.list_widget.hide()
        self.setFixedHeight(self._min_height)
        self.hide()

if __name__ == '__main__':
    app = QApplication(sys.argv)
    fastbar = FastBar()
    fastbar.hide()
    sys.exit(app.exec_())
