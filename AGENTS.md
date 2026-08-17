# ShopAutoBuyer - Core Agent Guidelines & Architecture Standards (AGENTS.md)

Tài liệu này là **Quy chuẩn kỹ thuật cốt lõi (Ground Truth)** dành cho các AI Agent và lập trình viên làm việc trên mã nguồn Plugin **ShopAutoBuyer** của ExileApi (tương thích cả Path of Exile 1 và Path of Exile 2).

Mọi thay đổi hoặc bổ sung tính năng trong tương lai **BẮT BUỘC** phải tuân theo các nguyên tắc được định nghĩa dưới đây.

---

## 1. Mục tiêu & Phạm vi dự án (Scope)

- **Mục tiêu:** Quét danh sách vật phẩm trong cửa sổ mua hàng của NPC (Merchant / Vendor Shop), đánh giá theo bộ lọc thông minh (Item Filters), vẽ giao diện trực quan (Highlight Overlay) và tự động thực hiện thao tác mua đồ an toàn (`Ctrl + Left Click`).
- **Phạm vi lưu trữ:** 100% mã nguồn, tài liệu, file cấu hình và tài nguyên của Plugin này **CHỈ NẰM TRONG** thư mục Git repository riêng biệt:
  `D:\codecuatien\ExileApi-Compiled\Plugins\Source\ShopAutoBuyer/`
  Tuyệt đối không được tạo file rác ra bên ngoài thư mục này.

---

## 2. Nguyên tắc bất biến (Core Invariants)

### Nguyên tắc 1: An toàn bộ nhớ & Chống Crash (Memory Safety & Exception Resilience)
- Đối tượng game trong `GameController`, `IngameState`, `IngameUi`, `Elements`, `Items` liên tục được cấp phát và giải phóng theo từng khung hình của game.
- **BẮT BUỘC:** 
  1. Mọi truy cập vào UI Elements hoặc Item Components phải thực hiện **Null Check** (`?.`) và kiểm tra cờ khả dụng (`element?.IsValid == true`, `element?.IsVisible == true`).
  2. Mọi logic quét hoặc vòng lặp xử lý danh sách item phải được bọc trong khối `try-catch` an toàn. 
  3. Lỗi phát sinh chỉ được ghi vào `LogHelper` (LogLevel Info/Debug/Error) của Plugin mà **KHÔNG ĐƯỢC PHÉP làm sập luồng Render chính** hoặc làm crash client ExileApi/Game.

### Nguyên tắc 2: Tương thích kép PoE 1 & PoE 2 (Adapter Pattern)
- Không bao giờ viết code phụ thuộc cứng (hardcode) vào cấu trúc UI của một phiên bản game duy nhất.
- Mọi tương tác với cửa sổ Shop NPC phải đi qua interface trừu tượng: **`IShopAdapter`**.
  - `Poe1ShopAdapter`: Chịu trách nhiệm cho PoE 1 (`PurchaseWindow`, `PurchaseWindowHideout`, hệ thống tiền tệ Orb, socket 6S/6L/RGB).
  - `Poe2ShopAdapter`: Chịu trách nhiệm cho PoE 2 (Merchant UI, cơ chế Gold, socket/uncut gem mới).
  - `ShopAdapterFactory`: Tự động nhận biết ngữ cảnh UI đang hoạt động và chuyển giao cho Adapter tương ứng.

### Nguyên tắc 3: Giả lập thao tác người dùng an toàn (Human-like Input Emulation)
- Trong Path of Exile, thao tác mua đồ từ Shop NPC là **`Ctrl + Click chuột trái`** lên vật phẩm.
- **BẮT BUỘC:**
  1. Thao tác mua phải chạy trong **Coroutine / SyncTask bất đồng bộ** (`ExileCore.Shared.Coroutine`). Tuyệt đối **KHÔNG** dùng `Thread.Sleep()` trong luồng chính (gây đơ giao diện game và treo HUD).
  2. Mỗi lần click mua phải có **Độ trễ ngẫu nhiên (Random Jitter Delay)** giữa các lần mua (mặc định 100ms - 250ms, có thể tùy chỉnh trong Settings) để mô phỏng hành vi bấm chuột tự nhiên của con người, tránh kích hoạt cơ chế phát hiện tự động hóa của server.
  3. Phải kiểm tra hành trang (Inventory) còn ô trống thích hợp trước khi thực hiện click mua.

### Nguyên tắc 4: Phân tách rõ ràng các tầng trách nhiệm (Separation of Concerns)
```
[UI / Settings Layer]  -->  ShopAutoBuyer.cs, ShopAutoBuyerSettings.cs (ImGui, Hotkeys, Render)
         ↓
[Adapter Layer]        -->  IShopAdapter, Poe1ShopAdapter, Poe2ShopAdapter (Đọc UI Game)
         ↓
[Engine & Logic Layer] -->  ItemFilterEngine (Lọc đồ), InventorySpaceChecker (Kiểm tra ô trống)
         ↓
[Execution Layer]      -->  PurchaseExecutor (Coroutine Ctrl+Click với Human Delay)
         ↓
[Utility Layer]        -->  MouseHelper, LogHelper (Hỗ trợ chuột, logging)
```

---

## 3. Tiêu chuẩn cấu trúc dữ liệu chuẩn hóa (`ShopItemInfo`)

Mọi Adapter (dù đọc từ PoE 1 hay PoE 2) đều phải chuẩn hóa dữ liệu item thu thập được về mô hình chung `ShopItemInfo`:
- `BaseName` (string): Tên phôi đồ (ví dụ: "Amethyst Ring", "Heavy Belt", "Uncut Skill Gem").
- `ItemLevel` (int): Cấp độ vật phẩm (ilvl).
- `Quality` (int): Chất lượng (0 - 20+).
- `Rarity` (ItemRarity): Normal, Magic, Rare, Unique.
- `Sockets` (int): Tổng số socket.
- `Links` (int): Số link liên kết lớn nhất.
- `IsRgb` (bool): Có liên kết 3 màu Red-Green-Blue (Chromatic recipe).
- `Cost` (CurrencyCost): Chi phí mua (loại tiền và số lượng: Gold hoặc Orbs).
- `ClickPosition` (SharpDX.Vector2 / System.Numerics.Vector2): Tọa độ tâm màn hình để click.
- `ScreenRect` (SharpDX.RectangleF): Khung chữ nhật hiển thị để vẽ Highlight.
- `TabIndex` (int): Vị trí tab chứa item trong Shop.

---

## 4. Hướng dẫn mở rộng bộ lọc (Extending Filter Rules)

Khi muốn bổ sung tiêu chí lọc mới (ví dụ: lọc theo cụm Mod cụ thể, Tier mod, hoặc loại tiền tệ mới):
1. Thêm trường tương ứng trong `FilterRule.cs` hoặc `ShopAutoBuyerSettings.cs`.
2. Mở rộng phương thức `ItemFilterEngine.Evaluate(ShopItemInfo item, FilterRule rule)`.
3. Đảm bảo quy tắc lọc mới trả về `true/false` an toàn và tương thích với cả `ShopItemInfo` của PoE 1 lẫn PoE 2.

---

## 5. Quy trình phát triển & Kiểm thử (Checklist cho Agent)

Trước khi hoàn tất bất kỳ phiên làm việc nào trên repo này:
- [ ] 100% file tạo/sửa đổi nằm trong `Plugins/Source/ShopAutoBuyer/`.
- [ ] Không có lỗi cú pháp C#; các tham chiếu DLL (`ExileCore`, `GameOffsets`, `ImGui.NET`, `SharpDX.Mathematics`) đúng namespace.
- [ ] Các khối gọi bộ nhớ game đều có null-check và bọc try-catch.
- [ ] Chạy `git status` bên trong thư mục plugin để kiểm tra các file đã được track đầy đủ.
