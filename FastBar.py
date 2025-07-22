# Gerekli kütüphaneleri içe aktarıyoruz.
import sys
import os
import webbrowser
import ctypes
from ctypes import wintypes
from threading import Thread

# PyQt5 bileşenlerini içe aktarıyoruz. Arayüzümüz burada şekillenecek.
from PyQt5.QtWidgets import (
    QApplication, QWidget, QLineEdit, QListWidget, QVBoxLayout, QStyle,
    QGraphicsDropShadowEffect
)
from PyQt5.QtGui import QColor
from PyQt5.QtCore import Qt, QTimer, pyqtSignal, QObject

# Alt + Space kısayolu için sabitler tanımlıyoruz
MOD_ALT = 0x0001
VK_SPACE = 0x20
HOTKEY_ID = 1
user32 = ctypes.windll.user32  # Windows API erişimi

# HotkeyListener sınıfı: sistem genelinde Alt+Space dinler
class HotkeyListener(QObject):
    hotkeyPressed = pyqtSignal()  # Kısayola basıldığında yayınlanacak sinyal

    def start(self):
        # Dinlemeyi ayrı bir iş parçacığında başlat
        Thread(target=self._listen, daemon=True).start()

    def _listen(self):
        if not user32.RegisterHotKey(None, HOTKEY_ID, MOD_ALT, VK_SPACE):
            print("Hotkey registration failed")  # Kayıt başarısızsa uyar
            return
        msg = wintypes.MSG()
        while user32.GetMessageW(ctypes.byref(msg), None, 0, 0) != 0:
            if msg.message == 0x0312 and msg.wParam == HOTKEY_ID:
                self.hotkeyPressed.emit()  # Tuş kombinasyonu basıldığında sinyali gönder
            user32.TranslateMessage(ctypes.byref(msg))
            user32.DispatchMessageW(ctypes.byref(msg))

# Ana uygulama penceresi
class FastBar(QWidget):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("Fastbar")
        self.setWindowFlags(Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint)
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.shortcuts = {}  # Uygulama kısayolları burada saklanacak

        self.initUI()  # Arayüzü kur
        QTimer.singleShot(0, self.build_index)  # Başlangıçta kısa bir gecikmeyle index oluştur

        self.hotkey = HotkeyListener()  # Kısayol dinleyicisini başlat
        self.hotkey.hotkeyPressed.connect(self.toggle_visibility)
        self.hotkey.start()

    def initUI(self):
        layout = QVBoxLayout()
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(5)

        # Arama kutusu
        self.input = QLineEdit(self)
        self.input.setPlaceholderText('Fastbar Search')
        self.input.setFixedHeight(60)

        # Sağ köşeye kapatma butonu
        close_icon = self.style().standardIcon(QStyle.SP_TitleBarCloseButton)
        close_action = self.input.addAction(close_icon, QLineEdit.TrailingPosition)
        close_action.triggered.connect(self.hide)

        # Olay bağlantıları
        self.input.textChanged.connect(self.on_text_changed)
        self.input.returnPressed.connect(self.on_return)

        # Arama kutusu stilleri
        self.input.setStyleSheet('''
            QLineEdit {
                background: rgba(255, 255, 255, 200);
                border-radius: 12px;
                padding: 16px;
                padding-right: 40px;
                font-size: 18px;
            }
        ''')

        # Arama kutusuna gölge efekti
        shadow = QGraphicsDropShadowEffect(self)
        shadow.setBlurRadius(15)
        shadow.setOffset(0)
        shadow.setColor(QColor(0, 0, 0, 100))
        self.input.setGraphicsEffect(shadow)

        # Listeleme alanı
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

        # Listeye de gölge efekti
        list_shadow = QGraphicsDropShadowEffect(self)
        list_shadow.setBlurRadius(15)
        list_shadow.setOffset(0)
        list_shadow.setColor(QColor(0, 0, 0, 100))
        self.list_widget.setGraphicsEffect(list_shadow)

        self.list_widget.hide()
        self.list_widget.itemActivated.connect(self.on_return)

        # Arayüz elemanlarını yerleştir
        layout.addWidget(self.input)
        layout.addWidget(self.list_widget)
        self.setLayout(layout)

        self.setFixedWidth(1000)  # Pencere genişliği sabit
        self._min_height = (
            self.input.height() +
            layout.contentsMargins().top() + layout.contentsMargins().bottom()
        )
        self.setFixedHeight(self._min_height)
        self.center()  # Ekranın üst kısmına ortala

    def center(self):
        # Pencereyi ekranın ortasına hizala
        screen = QApplication.primaryScreen().availableGeometry()
        x = (screen.width() - self.width()) // 2
        y = screen.height() // 4
        self.move(x, y)

    def build_index(self):
        # Başlat menüsünden tüm .lnk dosyalarını tarayıp index oluştur
        bases = filter(None, (os.getenv('APPDATA'), os.getenv('PROGRAMDATA')))
        for base in bases:
            sm = os.path.join(base, 'Microsoft', 'Windows', 'Start Menu', 'Programs')
            for root, _, files in os.walk(sm):
                for f in files:
                    if f.lower().endswith('.lnk'):
                        name = f[:-4]  # .lnk uzantısını sil
                        self.shortcuts[name] = os.path.join(root, f)

    def toggle_visibility(self):
        # Pencere açıkken gizle, kapalıysa göster
        if self.isVisible():
            self.hide()
        else:
            self.center()
            self.show()
            self.raise_()
            self.activateWindow()
            self.input.setFocus()

    def on_text_changed(self, text):
        # Arama kutusuna yazıldığında sonuçları güncelle
        self.list_widget.clear()
        if not text or not self.shortcuts:
            self.list_widget.hide()
            self.setFixedHeight(self._min_height)
            return

        # Eşleşenleri bul
        matches = [name for name in self.shortcuts if text.lower() in name.lower()]
        items = sorted(matches)[:7] if matches else [f"Search web for '{text}'"]

        self.list_widget.addItems(items)
        self.list_widget.setCurrentRow(0)
        self.list_widget.show()

        # Liste yüksekliği kadar pencereyi büyüt
        item_h = self.list_widget.sizeHintForRow(0)
        total_h = (
            self._min_height +
            item_h * self.list_widget.count() +
            self.layout().spacing()
        )
        self.setFixedHeight(total_h)

    def on_return(self):
        # Enter’a basıldığında yapılacak işlem
        current = self.list_widget.currentItem()
        query = self.input.text().strip()

        # Birebir eşleşen uygulama varsa çalıştır
        if query in self.shortcuts:
            os.startfile(self.shortcuts[query])
        elif current:
            text = current.text()
            if text.startswith("Search web for '"):
                # Web araması yap (Güvenli olması adına Duckduckgo )
                q = text.split("'")[1]
                webbrowser.open(f"https://duckduckgo.com/?q={q}")
            else:
                path = self.shortcuts.get(text)
                if path:
                    os.startfile(path)
        else:
            # Yazılan komut doğrudan çalıştırılabilir mi diye dener
            try:
                os.startfile(query)
            except Exception:
                pass

        # Temizle ve pencereyi kapatma kısmı burası
        self.input.clear()
        self.list_widget.hide()
        self.setFixedHeight(self._min_height)
        self.hide()

# Uygulama başlatma
if __name__ == '__main__':
    app = QApplication(sys.argv)
    fastbar = FastBar()
    fastbar.hide()  # Başlangıçta gizli başlar
    sys.exit(app.exec_())
