# Đánh giá kỹ thuật và tiến độ triển khai

Ngày rà soát: 16/09/2026. Đọc tài liệu gốc và toàn bộ Phase 00–06; thư mục `main` ban đầu chưa có mã nguồn.

## Kết luận khả thi

**Khả thi cho companion desktop với preset cấu hình trước và manual fallback.** C# / WPF phù hợp với Windows và overlay. Kiến trúc Domain → Application, với Detection/Infrastructure là adapter và App là composition root, cho phép kiểm thử nghiệp vụ độc lập.

**Auto detection vẫn là giả thuyết cần kiểm chứng**, chưa có screenshot, template, danh mục buff đã xác minh hoặc kết quả đo từ game. Không thể suy ra mọi buff/stack/duration từ một icon; tín hiệu không phân biệt được phải ở Manual hoặc Unknown. Ví dụ Woodpecker và thời lượng trong tài liệu chỉ là minh họa, không dùng làm dữ liệu game đã xác thực.

## Quyết định và điều chỉnh

| Vấn đề | Quyết định |
| --- | --- |
| .NET 8+ | Dùng .NET 10 LTS, SDK có sẵn 10.0.401; `global.json` cho phép SDK 10.0 feature band mới. .NET 10 được hỗ trợ tới 14/11/2028 theo [Microsoft](https://dotnet.microsoft.com/en-us/platform/support/policy). |
| Thứ tự tài liệu gốc khác Phase 01–06 | Các tài liệu phase là kế hoạch triển khai chính. Làm Phase 01 trước; Phase 02 và 03 độc lập một phần; sau đó 04 → 05 → 06. |
| Queue DropOldest cho event | Chỉ drop **raw frame** trước detector. Semantic event như Appeared/Disappeared/CharacterChanged không được mất âm thầm: bounded queue với backpressure; manual gửi thất bại phải báo rõ. |
| Refresh/Stack là trạng thái | Đây là hành động; trạng thái giữ Inactive/Pending/Active/Unknown/Expired theo Phase 03. Phase 01 mới có activate/refresh cơ bản, expire và reset. |
| Clock | Duration và CapturedAt dùng cùng clock monotonic; UTC chỉ để ghi log. Capture QPC cần chuyển về cùng miền thời gian khi tích hợp. Event đến trễ không được cấp lại toàn bộ duration. |
| Thread ownership | Một worker sở hữu state; UI polling snapshot immutable, không chờ Dispatcher từ worker. Start/Stop/Dispose tuần tự qua semaphore; mỗi lần chạy tạo session và inbox mới. |
| DI / MVVM | Constructor injection tại App startup; ICommand/INotifyPropertyChanged tối thiểu. Chưa cần DI container hoặc framework MVVM. Production Phase 01 chỉ dùng thư viện .NET; test dùng MSTest thay cho xUnit/NUnit đề xuất, cùng tiêu chí kiểm thử. |
| Logging | JSONL có level, sessionId, eventId và correlation trong message; rotation giới hạn kích thước. Adapter sau này có thể thay bằng Microsoft.Extensions.Logging/Serilog. |
| WPF overlay | Phase 02 kiểm chứng layered window, click-through và no-activate riêng biệt. `Topmost` không phải bằng chứng hoạt động trong exclusive fullscreen; ưu tiên kiểm thử windowed/borderless. Tham khảo [window features](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features) và [extended styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles). |
| Windows Graphics Capture | Phải kiểm tra IsSupported, xử lý resize/device lost, dispose frame và D3D resource. Capture cửa sổ trước, crop ROI trước CV; không mặc định API chỉ capture ROI. HDR cần xử lý riêng. Xem [Screen capture](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture). |
| Windows baseline | HWND capture API có từ Windows 10 1903 ([CreateForWindow](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)); hệ điều hành phát hành thực tế phải còn được .NET hỗ trợ. Môi trường dev hiện tại Windows build 26200 x64. |
| DPI và tọa độ | Tách WPF DIP, physical pixel của capture và normalized ROI. Cần ánh xạ client/content bounds, monitor DPI, letterbox và UI scale; normalized ROI một mình chưa đủ. |
| KPI 150 ms | Tách event→overlay khỏi capture→overlay. 10 FPS + debounce nhiều frame có thể vượt 150 ms trước khi event được phát. Đo percentile trên máy/dataset có ghi phiên bản. |
| Buff khi đổi nhân vật | Chính sách giữ buff toàn đội/off-field chưa được đặc tả; Phase 03/04 phải định nghĩa scope/owner trước khi hỗ trợ loại buff này. Không mặc định xóa mọi buff trong sản phẩm cuối. |
| Giới hạn tích hợp | Không memory read/inject/game hook. Điều đó giảm mức can thiệp nhưng không phải chứng nhận tương thích hoặc được nhà phát hành chấp thuận. |

## Phạm vi đợt triển khai đầu

Phase 01 nằm trong `main/ZZZBuffTracker.sln`:

- Sáu project App, Application, Domain, Detection, Infrastructure, Tests.
- Control Panel tiếng Việt với Start, Stop, ManualTrigger, Reset, preview và diagnostics cơ bản.
- FakeGameEventSource do người dùng kích hoạt, chưa có capture/OpenCV/global hotkey.
- IGameEventSource, IBuffStateStore, IPresetRepository, IClock và model bất biến. Repository JSON là Phase 04, hiện mới có contract.
- Worker có bounded event queue, cancellation, session/version filtering, confidence validation và timer monotonic.
- Dữ liệu demo chỉ có một buff 6 giây, không mang tên buff thật.

Ở thời điểm Phase 01, Rule Engine mới có trigger/timer cơ bản. Phase 03 hiện đã bổ sung dedup/cooldown, stack rules, Pending/Unknown, signal-loss policy và chống event đảo thứ tự; xem [bàn giao Phase 03](09-Phase-03-Rule-Engine-Va-Ban-Giao.md). Character mapping thuộc Phase 04.

## Các cổng kiểm chứng tiếp theo

| Phase | Trạng thái | Điều kiện để chốt |
| --- | --- | --- |
| 01 | Đã triển khai; kết quả kiểm tra ghi ở dưới | Build/test xanh, ứng dụng mở/đóng sạch, pipeline giả lập hoạt động |
| 02 | Đã triển khai tính năng; còn checklist thủ công | Overlay/preview, Play/Edit, hotkey, cấu hình và geometry đã có; cần smoke 100%/150% DPI và input trong game. Xem [Phase 02](08-Phase-02-Overlay-Va-Kiem-Thu.md) |
| 03 | Đã triển khai và kiểm thử nghiệp vụ | Rule engine deterministic, dedup bounded, stale/out-of-order event, stack/signal-loss tests, preset switching và benchmark; xem [Phase 03](09-Phase-03-Rule-Engine-Va-Ban-Giao.md) |
| 04 | Hoàn thành | Profile schema/migration, atomic save/backup/recovery, CRUD/import/export, character mapping và manual hotkey; 43/43 test và smoke WPF pass. Xem [Phase 04](10-Phase-04-Persistence-Preset-Hotkey.md) |
| 05 | Đã triển khai pipeline/công cụ; còn cổng chất lượng game | WGC, ROI/template, debounce, replay/evaluation; 58/58 tests, WGC smoke pass. Cần dataset ZZZ có nhãn và kiểm chứng game. Xem [Phase 05](11-Phase-05-Capture-Detection-Replay.md) |
| 06 | Chưa triển khai | E2E với game, gameplay dài, số liệu CPU/RAM/latency/FP/FN, publish và release checklist |

Phase 05 đã kiểm tra bằng fixture tổng hợp 1080p/1440p và WGC trên cửa sổ thử. Cần screenshot/replay hợp lệ từ game để chốt khả năng nhận diện thực tế.

## Kết quả kiểm chứng Phase 01

Trên Windows build 26200 x64, SDK 10.0.401:

- `dotnet build ZZZBuffTracker.sln --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test ZZZBuffTracker.sln --no-build --no-restore`: **8/8 pass**. Gồm activate/refresh/expiry/reset và snapshot bất biến; concurrent Start/repeated Stop/restart; session/version/confidence/timestamp filtering; late event; source fault và restart; dispose/cancellation; preset rỗng/không hợp lệ; kiểm tra dependency của core.
- `scripts/Smoke-Test.ps1`: **pass**, UI Automation điều khiển cửa sổ WPF thật với Start → ManualTrigger → Reset → ManualTrigger → đóng khi đang tracking, exit code 0.
- NuGet restore đã thành công; ứng dụng không cần package ngoài .NET, các dependency NuGet hiện chỉ phục vụ test.

Phase 01 đạt DoD nền tảng. Kết quả trên là tại thời điểm chốt Phase 01; khi đó chưa có overlay thật và chưa kiểm tra focus/DPI. Không suy rộng kết quả fake pipeline thành độ chính xác trên game.

## Cập nhật Phase 02

Đã thêm overlay thật Play/Edit, Text/Image/Hybrid, chỉnh settings trực tiếp, hotkey, lưu/nạp geometry và shared preview. Đặc tả triển khai, lệnh kiểm tra và giới hạn nằm trong [08 — Phase 02](08-Phase-02-Overlay-Va-Kiem-Thu.md). Pipeline vẫn giả lập; chỉ xử lý hiển thị Unknown, chưa triển khai rule tạo Unknown từ detection.

Build sạch, **14/14 test pass**, smoke WPF/Win32 pass (Play/Edit styles, focus khi show, settings/geometry reload, shutdown và hotkey dispatch/cleanup). Chưa đánh dấu hoàn tất phần smoke thủ công 100%/150% DPI và game thật.

## Cập nhật Phase 03

**Hoàn thành**: Rule Engine/state transitions, stack/refresh/reset/expire, Pending/Unknown và ba signal-loss policy, bounded dedup/cooldown, late/out-of-order handling, session/preset switching và diagnostics. Build sạch, **33/33 test pass**, benchmark Release khoảng **153.314 event/giây**, smoke WPF gồm các hành động rule mới pass. Không bao gồm capture/log/UI trong con số benchmark. Chi tiết và điểm tiếp tục Phase 04: [09 — Rule Engine và bàn giao](09-Phase-03-Rule-Engine-Va-Ban-Giao.md).

## Cập nhật Phase 04 — 17/09/2026

**Hoàn thành**: profile schema 2, migration và sample v1/v2, atomic write/backup/recovery, revision conflict protection, preset CRUD, import preview/confirm/export, CharacterChanged mapping/NotConfigured và manual fallback với 6 hotkey. Cấu hình overlay chuyển sang `profiles.json`, legacy `overlay.json` được giữ lại khi migration.

Build 0 warning/error; **43/43 test pass**; smoke WPF kiểm chứng create/update/apply preset, pause/manual hotkeys, profile/geometry reload, hotkey conflict/cleanup và regression các phase trước. Giới hạn và điểm tiếp tục capture/detection: [10 — Phase 04](10-Phase-04-Persistence-Preset-Hotkey.md).

## Cập nhật Phase 05 — 17/09/2026

Đã bổ sung Windows Graphics Capture window/monitor picker, client/ROI calibration, template pack có version, matching Gray8 có ngưỡng/hysteresis/debounce/cooldown, character arbitration, bounded frame queue, manual fallback khi CaptureUnavailable, preview/screenshot, recording/replay và evaluator có nhãn. Pause detection đóng capture resources. App target Windows 10 build 19041; core vẫn độc lập Windows.

Build sạch, 58/58 tests pass; WGC smoke trên cửa sổ thử nhận frame thật và phát hiện resize/close; fixture tổng hợp 1080p/1440p có TP=2, FP=0, FN=0 mỗi bộ. **Chưa chốt độ chính xác ZZZ:** thiếu dataset game có nhãn, kiểm tra monitor/HDR/DPI/game và profiling dài. Hướng dẫn, giới hạn và điểm tiếp tục: [11 — Phase 05](11-Phase-05-Capture-Detection-Replay.md).
