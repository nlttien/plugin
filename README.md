# ShopAutoBuyer - Plugin Tự Động Quét & Mua Đồ Trong Shop NPC (PoE 1 & PoE 2)

**ShopAutoBuyer** là plugin mở rộng dành cho framework **ExileApi** (PoeHUD / PoeHelper), hỗ trợ quét thông minh toàn bộ vật phẩm trong cửa sổ mua bán của NPC (Merchant / Vendor Shop) và tự động thực hiện thao tác mua (`Ctrl + Click`) một cách an toàn và nhanh chóng.

---

## 🌟 Tính Năng Nổi Bật

1. **Hỗ trợ song song cả Path of Exile 1 và Path of Exile 2:**
   - **PoE 1:** Tương thích với cửa sổ `PurchaseWindow`, `PurchaseWindowHideout`, hệ thống tiền tệ Orb (Alchemy, Chaos,...), lọc đồ 6-Socket, 6-Link, 3 màu RGB (Chromatic recipe).
   - **PoE 2:** Tương thích giao diện Merchant mới, cơ chế tính phí bằng **Gold (Vàng)**, hệ thống socket/gem mới của PoE 2.
   - **Auto-Detection:** Tự động nhận diện phiên bản UI đang mở hoặc tùy chọn ép kiểu trong Menu.

2. **Cơ chế mô phỏng chuột tự nhiên (Anti-Detection / Anti-Spam):**
   - Click mua chạy hoàn toàn trong luồng bất đồng bộ (Coroutine), không gây giật lag hay đơ game.
   - Hỗ trợ **Độ trễ ngẫu nhiên (Random Jitter Delay)** giữa các lần bấm chuột (100ms - 250ms), mô phỏng chính xác hành vi người dùng thật.
   - Tự động di chuột với độ lệch tọa độ ngẫu nhiên trong khung ô vật phẩm.

3. **Bảo vệ an toàn hành trang (Inventory Safety Check):**
   - Tự động kiểm tra kích thước vật phẩm và ô trống trong hành trang nhân vật trước khi mua. Tự động dừng an toàn khi đầy hòm đồ.

4. **Bộ lọc linh hoạt & Trực quan:**
   - **Visual Highlight:** Vẽ khung viền phát sáng và hiển thị tên vật phẩm thỏa điều kiện ngay trên màn hình game.
   - **Highlight Only Mode (Preview):** Chế độ chỉ xem trước đồ tìm được mà không tự động bấm mua.
   - **Scan All Tabs:** Tự động chuyển qua lần lượt các Tab trong shop để mua hết.
   - Lọc theo: Tên phôi đồ (Base name), Độ hiếm (Normal, Magic, Rare, Unique), Item Level (ilvl), Quality, Sockets, Links, RGB.

---

## 🚀 Hướng Dẫn Cài Đặt & Sử Dụng

### 1. Khởi động
1. Chạy game *Path of Exile 1* hoặc *Path of Exile 2* ở chế độ **Windowed** hoặc **Windowed Fullscreen (Borderless)**.
2. Mở file `Loader.exe` của ExileApi.
3. ExileApi sẽ tự động nhận diện và biên dịch source code từ thư mục `Plugins/Source/ShopAutoBuyer/`.

### 2. Phím tắt & Bảng điều khiển (F12)
- Nhấn phím **`F12`** trong game để mở menu cài đặt của ExileApi.
- Tìm mục **`Shop Auto Buyer (PoE 1 & 2)`**:
  - Gạt **`Enable`** để kích hoạt plugin.
  - Tùy chỉnh danh sách tên phôi đồ tại ô **`Base Names (Whitelist)`** (ví dụ: `Amethyst Ring, Heavy Belt, Two-Stone Ring, Uncut`).
  - Chọn phím tắt kích hoạt tại mục **`Auto-Buy Trigger Hotkey`** (Mặc định là **`F5`**).

### 3. Thực hiện mua đồ
1. Tương tác với NPC bất kỳ (ví dụ: Faustus, Helena, Vendor thị trấn) và chọn **Purchase Items / Mua đồ**.
2. Các món đồ thỏa điều kiện sẽ được vẽ khung viền xanh nổi bật trên màn hình.
3. Nhấn phím **`F5`** (hoặc bật tùy chọn *Auto Buy When Shop Opens*), plugin sẽ tự động di chuyển chuột và mua toàn bộ các món đồ được chọn vào hành trang của bạn!

---

## 📁 Cấu Trúc Mã Nguồn

```
ShopAutoBuyer/
├── AGENTS.md                  # Quy chuẩn kỹ thuật cốt lõi cho AI Agent & Developer
├── README.md                  # Hướng dẫn sử dụng
├── ShopAutoBuyer.csproj       # Project file C# SDK
├── ShopAutoBuyer.cs           # Entry point điều phối plugin
├── ShopAutoBuyerSettings.cs   # Bảng cấu hình ImGui
└── Core/
    ├── Models/                # Cấu trúc dữ liệu (ShopItemInfo, FilterRule, CurrencyCost, GameVersionEnum)
    ├── Adapters/              # Adapter Pattern (IShopAdapter, Poe1ShopAdapter, Poe2ShopAdapter)
    ├── Services/              # Services (ItemFilterEngine, InventorySpaceChecker, PurchaseExecutor)
    └── Utils/                 # Tiện ích (MouseHelper, LogHelper)
```
