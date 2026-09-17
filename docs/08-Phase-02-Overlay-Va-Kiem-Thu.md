# Phase 02 — Overlay và Control Panel

Lưu ý từ Phase 04: overlay được lưu chung trong `profiles.json`; `overlay.json` dưới đây mô tả phiên bản Phase 02 và được migration giữ nguyên file gốc. Xem [Phase 04](10-Phase-04-Persistence-Preset-Hotkey.md) để dùng cấu hình hiện tại.

## Phần đã triển khai

- `OverlayWindow`: WPF transparent/layered, topmost, ẩn taskbar, `ShowActivated=false`.
- Play Mode bật `WS_EX_TRANSPARENT` + `WS_EX_NOACTIVATE`; Edit Mode bỏ hai cờ này, hiện thanh kéo, nút Lưu & Play và góc resize. Cập nhật cửa sổ bằng `SetWindowPos` với `SWP_NOACTIVATE`. Nếu đổi style thất bại, overlay tự ẩn và báo lỗi để không chặn input.
- `OverlayContent` và `OverlayViewModel` dùng chung cho cửa sổ thật và preview. Item ViewModel giữ nguyên identity khi countdown cập nhật, tránh dựng lại toàn bộ cây giao diện mỗi tick.
- Text, Image, Hybrid; icon hiện tại là vector disc minh họa tự tạo, chưa phải asset ZZZ. Có countdown, progress, stack, tùy chọn ẩn Inactive/Expired; Unknown/Pending hiện `?` và không hiện progress giả.
- Scale 0.5–2, opacity 0.2–1, spacing 0–32 DIP, hướng dọc/ngang thay đổi trực tiếp.
- Chế độ **Mẫu 3 buff** hiển thị Active, Unknown, Inactive trên cả hai cửa sổ, có nhãn DEMO. Đây là snapshot minh họa tĩnh, không phát event hoặc thay đổi tracking; tắt mẫu để xem countdown thực tế.
- Ctrl+Alt+O ẩn/hiện; Ctrl+Alt+E vào Edit hoặc lưu rồi về Play; Ctrl+Alt+R reset buff. Đăng ký bằng RegisterHotKey/WM_HOTKEY, không hook bàn phím/game. Báo xung đột trong Control Panel và hủy đăng ký khi đóng.
- Cấu hình/geometry lưu qua Application service và JSON repository; ghi file tạm rồi thay thế, giữ bản `.bak`. Validate schema, enum, giá trị số hữu hạn và kích thước. File hỏng được giữ nguyên tới khi người dùng bấm Lưu.
- Có manifest PerMonitorV2. Geometry dùng DIP; vị trí ngoài virtual desktop được đưa về màn hình chính, có nút khôi phục vị trí cho trường hợp đổi monitor hoặc overlay bị khuất.

## Cách dùng

1. Chạy ứng dụng, bấm **Bắt đầu**, rồi **Kích hoạt / làm mới**. Overlay và preview cùng hiển thị buff demo.
2. Bấm **Play / Edit** hoặc Ctrl+Alt+E. Kéo thanh trên cùng để di chuyển, kéo góc dưới phải để resize.
3. Chỉnh hiển thị, scale, opacity, spacing và hướng tại Control Panel.
4. Bấm **Lưu cấu hình & Play**, nút **Lưu & Play** trên overlay, hoặc Ctrl+Alt+E khi đang Edit.
5. Nếu không tìm thấy cửa sổ, bấm **Đưa overlay về màn hình chính**. Nếu nội dung bị cắt do scale/hướng, mở Edit và tăng kích thước khung; có thể cuộn trong Edit.

Mọi thay đổi có hiệu lực ngay nhưng **chỉ lưu khi bấm Lưu hoặc rời Edit qua Ctrl+Alt+E/Play–Edit**. Đóng ứng dụng không tự ghi đè cấu hình chưa lưu. Khi mở lại luôn bắt đầu ở Play; không lưu chế độ mẫu, trạng thái buff hay Edit Mode.

File: `%LOCALAPPDATA%/ZZZBuffTracker/overlay.json`, bản cũ `overlay.json.bak`. Dùng `--data-dir <thư-mục>` khi chạy để tách dữ liệu thử nghiệm khỏi cấu hình cá nhân.

## Kiểm tra tự động

Từ `main`:

```powershell
dotnet build ZZZBuffTracker.sln --no-restore
dotnet test ZZZBuffTracker.sln --no-build --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Smoke-Test.ps1
```

Unit/integration test gồm snapshot 0/1/nhiều buff, Unknown/Pending, hide inactive, clamp countdown/progress, validate geometry, round-trip JSON/backup, corrupt JSON, cùng các test lifecycle/timer Phase 01.

Smoke script dùng dữ liệu riêng trong `main/artifacts/smoke-*`, UI Automation và Win32: tracking/expiry, snapshot preview/overlay, thay display/settings trực tiếp, cờ Play/Edit, xử lý thông điệp hotkey, không đổi foreground khi hiện Play overlay, save/reload geometry và đóng ở trạng thái đang tracking lẫn chưa Start. Không gửi phím/chuột vào game.

### Kết quả ngày 16/09/2026

- Build toàn solution: **0 warning, 0 error**.
- Unit/integration: **14/14 pass** (8 test Phase 01 và 6 test mới).
- Smoke WPF/Win32: **pass**; hai lần chạy ứng dụng đều thoát với code 0. Dữ liệu kiểm tra: `main/artifacts/smoke-92a380126d174d96b637d98f424aef0a` (không đưa vào source control).
- Smoke đã phát hiện và kiểm chứng bản sửa lỗi đóng cửa sổ khi chưa Start: tránh gọi `Close()` lồng trong `Closing`, chuyển lần đóng cuối sang lượt Dispatcher tiếp theo.
- Kiểm tra hotkey tự động ở mức đăng ký, dispatch `WM_HOTKEY` và giải phóng; chưa thay thế thao tác nhấn phím vật lý trong game. Kiểm tra Play tự động xác minh style/no-activate và foreground khi show; click xuyên trên game vẫn thuộc checklist dưới.

Tính năng Phase 02 và kiểm tra tự động đã hoàn tất; phần xác nhận thủ công chưa chốt.

## Checklist thủ công còn cần

- [ ] Windows scale 100%: kéo/resize/lưu/mở lại; text/icon rõ, Control Panel không cắt nút.
- [ ] Windows scale 150%: các thao tác tương tự, đối chiếu vị trí DIP và kích thước hiển thị.
- [ ] Kéo giữa hai monitor có DPI khác nhau; tháo monitor hoặc đổi bố cục; nút khôi phục luôn đưa overlay về vùng dùng được.
- [ ] Trong game windowed/borderless: Play click xuyên cả vùng buff; không mất focus khi tick, ẩn/hiện và reset.
- [ ] Nhấn tổ hợp hotkey vật lý khi game đang foreground; kiểm tra xung đột với ứng dụng khác.
- [ ] Chơi thử dài, xác nhận chi phí render/click-through và thao tác thoát Edit thuận tiện.

Chưa tuyên bố hỗ trợ exclusive fullscreen. Cờ Win32 và smoke focus trên desktop không thay thế việc kiểm tra input trong game. Capture/detection và dữ liệu buff ZZZ thật nằm ở phase sau.

## Căn cứ kỹ thuật

- [Microsoft: Layered windows và click-through](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#layered-windows).
- [Microsoft: Extended window styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles).
- [Microsoft: RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey).
