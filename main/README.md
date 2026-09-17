# ZZZ Buff Tracker — Phase 01–05

Desktop companion Windows, C# / .NET 10 / WPF. Có manual fallback và pipeline **Windows Graphics Capture → template detection → Rule Engine**. Chưa có template/dataset ZZZ được xác minh; cần hiệu chỉnh trước khi dùng auto detection với game.

## Chạy

Cần .NET 10 SDK và Windows 10 build 19041 trở lên hoặc Windows 11. Từ thư mục `main`:

```powershell
dotnet restore ZZZBuffTracker.sln
dotnet build ZZZBuffTracker.sln --no-restore
dotnet test ZZZBuffTracker.sln --no-build
dotnet run --project .\ZZZBuffTracker.App\ZZZBuffTracker.App.csproj
```

**Chọn đúng đường dẫn theo thư mục hiện tại của terminal** (`Get-Location` để kiểm tra):

| Terminal đang ở | Lệnh chạy |
| --- | --- |
| `zzz-tracking-disc/main` | `dotnet run --project .\ZZZBuffTracker.App\ZZZBuffTracker.App.csproj` |
| `zzz-tracking-disc` | `dotnet run --project .\main\ZZZBuffTracker.App\ZZZBuffTracker.App.csproj` |

Nếu gặp `The provided file path does not exist`, kiểm tra thư mục terminal và dùng lệnh tương ứng ở trên. Không thêm `main/` lần nữa khi đã ở trong `main`, và không thêm dấu chấm cuối câu vào lệnh.

Smoke test WPF tự động (Windows, sau khi build Debug):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Smoke-Test.ps1
```

Script dùng UI Automation/Win32 để kiểm tra tracking, expiry, preview/overlay, Unknown, Play/Edit, thay đổi settings, lưu/nạp geometry, hotkey dispatch/cleanup và shutdown. Dữ liệu được tách trong `artifacts/smoke-*`. Không thay thế kiểm thử thủ công DPI và input trong game.

Kết quả Phase 02: build 0 warning/error, **14/14 test pass**, smoke WPF/Win32 pass, cả hai lần mở/đóng đều exit code 0.

Khi chốt Phase 03: **33/33 test pass**, build sạch, smoke WPF với các hành động rule mới pass, và benchmark Rule Engine Release khoảng 153.000 event/giây (100.000 event, không gồm log/UI/capture). Chi tiết: [bàn giao Phase 03](../docs/09-Phase-03-Rule-Engine-Va-Ban-Giao.md).

Khi chốt Phase 04: **43/43 test pass**, build 0 warning/error, smoke WPF/Win32 pass với tạo/sửa/áp dụng preset, profile reload, manual khi detection pause, hotkey conflict và cleanup. Xem [bàn giao Phase 04](../docs/10-Phase-04-Persistence-Preset-Hotkey.md).

Phase 05: **58/58 test pass**, build sạch, WGC smoke nhận frame thật từ cửa sổ thử và xử lý resize/close. Fixture tổng hợp 1080p/1440p pass; độ chính xác game chưa được kiểm chứng. Xem [hướng dẫn và bàn giao Phase 05](../docs/11-Phase-05-Capture-Detection-Replay.md).

Nếu môi trường giới hạn ghi user profile, đặt `DOTNET_CLI_HOME` tới một thư mục có quyền ghi trước khi chạy; môi trường triển khai này dùng `main/.dotnet-local`.

1. Bấm **Bắt đầu**. Trạng thái chuyển thành Manual / Giả lập.
2. Bấm **Kích hoạt / làm mới**: buff demo chạy 6 giây; kích hoạt lại làm mới timer.
3. **Reset** đưa buff về Inactive; **Dừng** hủy nguồn sự kiện và worker, xóa preview.
4. Bắt đầu lại tạo session mới. Đóng cửa sổ chờ worker hủy xong rồi thoát.

## Overlay

- **Ctrl+Alt+O**: ẩn/hiện overlay.
- **Ctrl+Alt+E**: vào Edit, hoặc lưu và trở lại Play. Edit cho phép kéo thanh tiêu đề và resize góc dưới phải.
- **Ctrl+Alt+R**: reset buff. Nếu hotkey xung đột, dùng nút tương ứng trong Control Panel.
- Text/Image/Hybrid, scale, opacity, hướng và khoảng cách áp dụng ngay cho cả preview lẫn overlay. Icon hiện là hình disc demo.
- **Mẫu 3 buff** là dữ liệu tĩnh Active/Unknown/Inactive để thử giao diện; tắt để theo dõi timer từ pipeline.
- **Lưu cấu hình & Play** ghi phần Overlay trong `%LOCALAPPDATA%/ZZZBuffTracker/profiles.json`, giữ `.bak`. `overlay.json` cũ được migration khi chưa có profile. Không tự lưu khi đóng; mở lại luôn vào Play.
- **Đưa overlay về màn hình chính** khôi phục vị trí khi bị khuất. Nếu scale lớn hoặc xếp ngang làm nội dung bị cắt, tăng kích thước khung trong Edit.

Dữ liệu dev có thể tách riêng:

```powershell
dotnet run --project ZZZBuffTracker.App -- --data-dir ./artifacts/manual-profile
```

Hướng dẫn và phần kiểm tra còn lại: [Phase 02](../docs/08-Phase-02-Overlay-Va-Kiem-Thu.md).

## Thử Rule Engine

Sau khi Bắt đầu, mở **Thử Rule Engine (dữ liệu giả lập)** và tắt **Mẫu 3 buff** để thấy state từ pipeline. Có các nút Pending, Stack +1 (tối đa 3), Refresh (không tăng stack), Mất tín hiệu (Unknown), Deactivate và Expire. Demo dùng policy KeepUnknown; Refresh/Activate xác nhận lại sẽ tiếp tục timer 6 giây. Reset xóa state.

Engine hỗ trợ custom BuffRule theo event/subject, confidence, correlation cooldown, session/version, event đến trễ/sai thứ tự và ba policy mất tín hiệu. Phase 04 đã nối repository/CharacterChanged với session switching.

## Presets / Profiles (Phase 04)

Mở **Presets / Profiles** trên Dashboard để tạo/chọn/sửa/xóa preset, thêm/bỏ buff và chỉnh duration/stack/policy. **Dùng nhân vật này** áp dụng preset; ID không có preset hiển thị NotConfigured. **Giả lập CharacterChanged** thử auto mapping khi tracking và detection đang bật.

**Chọn file import** chỉ kiểm tra và preview; **Áp dụng import** yêu cầu xác nhận thay toàn bộ profile và tạo backup. **Export profile** có xác nhận khi đè file. Thử [sample schema 2](samples/profile-v2.json) hoặc [sample schema 1](samples/profile-v1.json). Custom rule và danh mục disc có thể chỉnh qua file rồi import; UI hiện chỉnh các dòng buff, không phải trình thiết kế rule.

Hotkey bổ sung: **Ctrl+Alt+T** activate, **Ctrl+Alt+F** refresh, **Ctrl+Alt+D** bật/tạm dừng detection. Manual vẫn dùng khi detection dừng. Nếu buff đang chọn trong bảng không thuộc session hiện tại, trigger nhắm vào buff đầu của session.

Lưu profile có kiểm tra revision; nếu đã lưu overlay hoặc file thay đổi trong lúc sửa preset, bấm **Nạp lại** trước khi sửa/lưu lại. Primary JSON hỏng được khôi phục từ `.bak` nếu hợp lệ và bản hỏng được lưu `.corrupt-*`. Không có backup hợp lệ thì báo lỗi, không ghi đè âm thầm. Chi tiết: [bàn giao Phase 04](../docs/10-Phase-04-Persistence-Preset-Hotkey.md).

Chạy riêng benchmark từ `main`:

```powershell
dotnet test ZZZBuffTracker.Tests/ZZZBuffTracker.Tests.csproj -c Release --filter TestCategory=Benchmark --logger "console;verbosity=detailed"
```

Log: `%LOCALAPPDATA%/ZZZBuffTracker/logs/tracking.jsonl`; có session/event ID, level và lỗi. File cũ chuyển thành `.previous` khi vượt khoảng 2 MB.

## Ranh giới module

```text
App (WPF / MVVM / composition root)
 ├─ Detection (template detector, bounded capture/manual source, replay/evaluation)
 ├─ Infrastructure (clock, snapshot store, log, versioned profile JSON)
 └─ Application (lifecycle, single-writer pipeline, profile service, character mapping)
     └─ Domain (immutable models)
Tests → Application / Detection / Infrastructure; không cần WPF hoặc game
```

Start/Stop là idempotent; snapshot qua Volatile.Read/Write. Manual inbox giới hạn 64, worker queue 128 và backpressure. UI lấy snapshot mới nhất mỗi 50 ms; worker chiếu countdown mỗi 33 ms. Đây chưa phải kết quả đo latency thực tế.

## Capture / Detection (Phase 05)

Mở **Capture / Detection** → **Chọn cửa sổ / màn hình** → **Bắt đầu tracking**. Chưa nạp pack thì chỉ preview. Dùng **Giữ ảnh để hiệu chỉnh**, nhập Client area và ROI theo X,Y,W,H trong [0,1], tạo template Buff/Character với Subject ID tương ứng preset/rule, rồi **Lưu pack thành file mới**. Có thể mở screenshot và Test score offline. Template pack cần nạp lại ở phiên app sau; không tự capture khi khởi động.

Matching dùng ROI đã căn chỉnh; không tự tìm icon di chuyển. Đổi UI scale/layout cần hiệu chỉnh lại. Pause bằng Ctrl+Alt+D đóng capture resources, giữ manual/timer. Lỗi capture/resize hiển thị CaptureUnavailable và cho dùng manual; chọn lại nguồn để thử lại. Chưa có template ZZZ mặc định.

**Ghi replay 3 giây** lưu dataset local có pack/version; **Mở replay.json** phát lại qua cùng detector. **Đánh giá replay có nhãn** đo TP/FP/FN offline, không sửa tracking. Expected=null là chưa có nhãn, không được tính đúng/sai. Chi tiết schema, hướng dẫn hiệu chỉnh và phần kiểm chứng còn lại: [Phase 05](../docs/11-Phase-05-Capture-Detection-Replay.md).

```powershell
# Sau build, đóng app trước khi build lại để tránh khóa exe
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Capture-Smoke-Test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/New-SyntheticReplay.ps1
```

Capture smoke chỉ quay cửa sổ checkerboard tự tạo. Nếu chạy trong sandbox gặp COM 0x80070424, thử lệnh từ terminal Windows bình thường; không cần sửa service để chạy manual/replay. Fixture tổng hợp không thay thế dataset game. Kế hoạch và đánh giá kỹ thuật: [docs/07](../docs/07-Danh-Gia-Kha-Thi-Va-Tien-Do.md).
