# Phase 05 — Capture, Detection và Replay

Ngày triển khai: 17/09/2026.

**Đã triển khai pipeline và công cụ hiệu chỉnh; chưa chốt độ chính xác trên ZZZ.** Có Windows Graphics Capture (WGC), template matching, xác nhận nhiều frame, nối Rule Engine, manual fallback, screenshot/ROI preview, ghi/phát/đánh giá replay. Chưa có template hay dataset game được xác minh. Không coi kết quả fixture tổng hợp là chứng nhận nhận diện buff thật.

## Chạy và sử dụng

Yêu cầu Windows 10 build 19041 trở lên hoặc Windows 11, .NET 10 SDK. App chuyển target từ `net10.0-windows` sang `net10.0-windows10.0.19041.0` để dùng WinRT projection của Windows SDK. Sau cập nhật cần restore một lần; Domain/Application/Detection/Tests vẫn `net10.0`, không phụ thuộc WPF/WinRT.

```powershell
# Terminal tại main
dotnet restore ZZZBuffTracker.sln
dotnet run --project .\ZZZBuffTracker.App\ZZZBuffTracker.App.csproj
```

1. Trong **Presets / Profiles**, tạo/chọn preset. Subject ID của template buff phải trùng subject của rule (mặc định là Buff ID); template Character dùng Character ID để tìm preset. Dữ liệu demo vẫn là minh họa.
2. Mở **Capture / Detection**, chọn **Chọn cửa sổ / màn hình**. Windows hiển thị picker; hủy picker giữ nguyên nguồn trước đó. Chọn cửa sổ game để tránh thu overlay hoặc nội dung cửa sổ khác vào ROI.
3. **Bắt đầu tracking**. Chưa có pack thì chỉ thu frame/preview, không phát event nhận diện. `Ctrl+Alt+D` phải đang bật detection. Tắt **Mẫu 3 buff** ở Dashboard để quan sát state thật từ pipeline.
4. **Giữ ảnh để hiệu chỉnh** đóng băng preview, không dừng capture. Hoặc **Mở screenshot** PNG/JPEG/BMP để hiệu chỉnh offline. Screenshot không phát event vào tracking.
5. Nhập **Client X,Y,W,H** chuẩn hóa theo toàn bộ ảnh capture, loại bỏ titlebar/viền/letterbox nếu có. WGC picker không trả HWND để tự suy ra client area; phiên bản này yêu cầu hiệu chỉnh thủ công. Borderless toàn ảnh dùng `0,0,1,1`. Với monitor capture, client area là vùng game trên monitor đó.
6. Nhập **ROI X,Y,W,H** chuẩn hóa theo client đã crop. Khoanh sát một icon/portrait, xem ảnh bên phải bằng **Xem ROI**. Dùng dấu chấm thập phân, dấu phẩy phân cách bốn giá trị. Đây là nhập tọa độ số, chưa có kéo chọn ROI bằng chuột.
7. Nhập Template ID, Subject ID, loại Buff/Character. Character scope chỉ áp dụng với Buff; scope trống áp dụng mọi preset. UI scale là metadata của pack; thay UI scale trong game cần hiệu chỉnh pack mới.
8. **Thêm / thay template từ ROI** tạo Gray8 template 32×32 từ ảnh hiện tại. Tăng version template/pack khi thay; ID template không đổi sẽ thay template đó. Template quá phẳng hoặc cấu hình không hợp lệ bị từ chối. **Pack mới** xóa pack đang giữ trong bộ nhớ để hiệu chỉnh geometry/scale khác; không xóa file.
9. **Test score trên ảnh hiện tại** tính similarity, không gửi event và không giả định một ảnh đủ để xác nhận. Thử cả ảnh có icon, không có icon, icon tương tự, animation/blur/che khuất. Mặc định On=0.94, Off=0.70, 3 frame liên tiếp và cooldown 300 ms; phải hiệu chỉnh bằng dữ liệu thật.
10. **Lưu pack thành file mới**, rồi **Nạp template pack** ở lần mở ứng dụng sau. Không tự nhớ window/monitor hay tự khởi động capture khi app mở lại. Lưu không ghi đè file pack có sẵn: chọn tên/version mới.

**Chỉ manual** ngắt capture. **Dừng** trên Dashboard hủy cả nguồn capture và tracking; pause detection chỉ dừng capture, giữ timer/manual. Đóng cửa sổ Capture chỉ đóng công cụ, capture tiếp tục theo tracking. Mở lại công cụ khôi phục pack/nguồn đang chọn trong phiên hiện tại.

## Pipeline và quyết định kỹ thuật

```text
OS picker → WindowsCaptureSource → GrayFrame (owned Gray8 pixels)
                                    ↓ bounded 2, DropOldest
                              TemplateDetector
                                    ↓ semantic events, Wait/backpressure
Manual controls ──────────────→ CaptureEventSource
                                    ↓ session + preset version
                              TrackingService → Rule Engine → snapshot → overlay
```

- WGC dùng `Direct3D11CaptureFramePool.CreateFreeThreaded`, hai GPU buffer, D3D11 BGRA hardware device; thử WARP nếu tạo hardware device lỗi. Poll tối đa khoảng 10 Hz, lấy frame mới hơn trước CPU readback qua `SoftwareBitmap.CreateCopyFromSurfaceAsync`. Surface/bitmap/device/pool/session được dispose; channel chỉ giữ bộ nhớ managed.
- Timestamp frame lấy `SystemRelativeTime` (QPC), quy đổi về `IClock.Elapsed`; tính latency từ captured time tới sau detector/event enqueue. Không dùng thời gian UTC cho timer/rule.
- Queue raw frame 2 phần tử DropOldest; queue semantic 64 và queue Application 128 dùng backpressure. Manual inbox đầy trả false để UI báo thử lại. Diagnostics đếm frame xử lý, drop ở queue managed, latency và score/quyết định từng template. Không đếm frame bị bỏ bên trong GPU pool.
- Detector độc lập với Windows, dùng cùng `GrayFrame` cho WGC, fixture và replay. Matching là **so khớp ROI đã căn chỉnh**, resize nearest-sample về template rồi tính `1 - MAE/255`. Không quét toàn màn hình, không OCR, không tự tìm icon dịch chuyển trong ROI. Không dùng OpenCV trong đợt này. Đây là lựa chọn đơn giản, bounded và kiểm chứng được; nâng matcher theo dataset nếu cần.
- ROI/pack reference dimension tính trên client đã crop. Cùng aspect ratio cho phép scale theo resolution, ví dụ 1080p → 1440p. Sai aspect ratio >2.5% bị từ chối; resize giữa phiên cũng bị từ chối và yêu cầu chọn lại nguồn/kiểm tra ROI. Không tự suy ra UI scale/layout mới.
- Buff: score >= On là present; <= Off là absent; giữa hai ngưỡng là ambiguous. Mỗi trạng thái cần N frame liên tiếp, cooldown dùng captured time. Icon giữ nguyên không phát lặp. Frame lặp/sai thứ tự bị bỏ; khoảng cách >500 ms reset đếm xác nhận. Sau ambiguous, cần xác nhận lại trước xuất hiện/biến mất.
- Character: chỉ chọn template đạt max(On, 0.9) và hơn ứng viên thứ hai >=0.05; trường hợp ngang điểm không đổi nhân vật. CharacterChanged đi qua repository của Application, hủy producer cũ và tạo session/detector mới. Buff template có CharacterId chỉ chạy với nhân vật hiện tại. Không thay state trực tiếp trong Detection.
- Confidence present là similarity. Với absent/ambiguous đã được N frame xác nhận, event confidence hiện là 1 (mức tin của quyết định theo rule), **không phải xác suất thống kê**; raw score giữ trong Evidence. Cần calibration trên game, đặc biệt che khuất có thể bị xem là absent nếu score xuống thấp.
- Evidence chứa pack/template ID và version, sequence, kích thước frame, ROI, similarity và processed time. Source phân biệt WindowsCaptureSource/ReplayFrameSource. Log event evidence; log score/debounce/cooldown khoảng mỗi 10 frame; UI hiển thị quyết định mới nhất. Pack lưu ClientArea/UI scale để diễn giải lại evidence.
- Capture đóng/mất, resize, frame timeout 3 giây, ảnh gần như đơn sắc/đen hoặc lỗi API → `CaptureUnavailable` trong capture diagnostics; tracking và manual vẫn sống. Phát `SignalLost`, không suy diễn lỗi capture thành `BuffIconDisappeared`; policy của buff quyết định timer/Unknown. Cần chọn lại nguồn hoặc pause/resume để thử lại, không retry vô hạn. WGC có thể không phát frame khi nội dung đứng yên: timeout bảo thủ cũng có thể xảy ra ở menu tĩnh.

`TrackingStatus` vẫn phản ánh lifecycle/preset (Manual/NotConfigured/Faulted/Stopped); sức khỏe capture là trạng thái riêng hiển thị trên Dashboard/Capture. Khi chỉ preview, trạng thái Capturing không có nghĩa đã cấu hình detector hay phát event.

Giới hạn: tối đa 7680×4320/frame, 64 template/pack, template 4–128 pixel mỗi chiều, pack <=2 MB. 10 Hz + N=3 cho độ trễ xác nhận khoảng 200–300 ms trước overhead; chưa tối ưu mục tiêu realtime dưới 100 ms. CPU readback full frame có chi phí RAM/băng thông, cần profiling gameplay ở Phase 06.

## Replay và đánh giá có nhãn

**Ghi replay 3 giây** lấy frame mới nhất mỗi 100 ms, tối đa 30 frame và 128 MB. Chọn thư mục cha; app tạo thư mục dataset mới. Nếu đổi pack/nguồn trong lúc ghi thì từ chối để tránh gắn sai metadata. Recording thu toàn bộ frame đã chọn, không chỉ ROI. Không tự upload dataset.

```text
dataset/
  templates.json
  replay.json
  frame-0000.gray
  ...
```

- `.gray`: little-endian magic `0x315A5A47`, int32 Width, int32 Height, theo sau đúng Width×Height byte Gray8, không padding. Kiểm tra header/length trước allocation.
- `replay.json`: SchemaVersion=1, Description, TemplatePack (tên file), Frames gồm File, OffsetMilliseconds và Expected. Timestamp tăng nghiêm ngặt, tối đa 60 giây; tối đa 300 frame/128 MB cho dataset nạp từ ngoài. Chỉ cho tên file trong cùng thư mục, từ chối đường dẫn `../` và absolute.
- Pack/manifest chưa ghi xong hoặc thao tác bị hủy có thể để lại thư mục dở dang; `replay.json` được ghi cuối để không xem recording thiếu frame là hoàn chỉnh. Không tự xóa dữ liệu đã ghi.
- **Mở replay.json** chạy lại theo timestamp gốc vào tracking hiện tại, với session/version hiện tại. Replay cũng đi qua queue DropOldest nếu xử lý chậm. Source kết thúc → ReplayComplete; timer/manual tiếp tục. Mở lại replay để phát lại.
- **Đánh giá replay có nhãn** chạy offline tất cả frame, không drop, không thay tracking. Character scope lấy từ ô Character scope; evaluator đổi context/detector khi xác nhận nhân vật mới, không chạy Rule Engine. Đây là đánh giá detector, không phải đánh giá buff state.
- `Expected: null` là chưa có nhãn, không được tính đúng/sai. `Expected: ""` là không có event. Ví dụ `"BuffIconAppeared:demo-buff"` hoặc nhiều event phân cách dấu phẩy. Nhãn là **event trên frame xác nhận**, không phải nhãn “icon đang hiện” trên mọi frame. TP/FP/FN so khớp chính xác type/subject/frame, chưa hỗ trợ tolerance theo thời gian.

CLI đánh giá, không mở Dashboard:

```powershell
$app = '.\ZZZBuffTracker.App\bin\Debug\net10.0-windows10.0.19041.0\ZZZBuffTracker.App.exe'
# Trong PowerShell, dùng Start-Process -Wait để đợi WinExe và đọc report sau khi hoàn tất.
Start-Process -FilePath $app -ArgumentList '--evaluate-replay "C:\dataset\replay.json" "C:\dataset\evaluation.json" demo-character' -Wait
```

Exit 0 nếu có ít nhất một frame gán nhãn và không có FP/FN; exit 1 nếu không có nhãn, có mismatch hoặc lỗi. Report chứa số frame, TP/FP/FN, thời gian matcher (không gồm đọc file/readback/UI) và mismatch. Không diễn giải dataset gán nhãn một phần thành độ chính xác toàn bộ.

## Kiểm chứng

```powershell
# Trong main; đóng app trước khi build để tránh file exe bị khóa
dotnet build ZZZBuffTracker.sln --no-restore
dotnet test ZZZBuffTracker.sln --no-build --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Smoke-Test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Capture-Smoke-Test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/New-SyntheticReplay.ps1
```

- Build: 0 warning/error. **58/58 unit/integration tests pass**, gồm regression Phase 01–04 và 15 test detection: debounce/dedup, ambiguity, icon tương tự/flicker, cooldown, stale/gap, character arbitration, ROI 1080p/1440p, resize/aspect mismatch, validation, template/replay round-trip/traversal, lỗi capture/manual/Unknown, pause/resume/dispose, bounded frame dropping, auto character → preset → buff, đánh giá nhãn TP/FP/FN.
- WGC smoke **pass** ngoài sandbox trên Windows hiện tại: capture cửa sổ checkerboard tự tạo có heartbeat, nhận 5 frame 400×400 physical pixels (cửa sổ 320 DIP ở 125%), đúng 1 appearance, resize và target close bị phát hiện. Lưu `capture.png`, dataset và result.json tại `main/artifacts/capture-4cd34f092b4542bd94c6750dbda552a4`.
- Trong sandbox, CreateForWindow từng trả COM `0x80070424`; chạy cùng kiểm tra ngoài sandbox thành công. Không sửa/bật/tắt Windows service. Đây là giới hạn môi trường kiểm tra, không kết luận WGC hỏng trên máy người dùng. Cửa sổ thử có animation vì WGC không đảm bảo gửi liên tục frame giống nhau.
- Hai dataset tổng hợp do `New-SyntheticReplay.ps1` tạo: mỗi resolution 8 frame có nhãn, **TP=2, FP=0, FN=0**. Matcher tổng khoảng 20.9 ms/8 frame ở 1080p và 25.6 ms/8 frame ở 1440p trong lần đo này. Không phải benchmark end-to-end hoặc số liệu game. Fixture/script không sử dụng hình ảnh ZZZ.
- Smoke WPF/Win32 **pass**, gồm mở/đóng Capture window và regression overlay/rules/profile/hotkey/persistence/shutdown. Artifact: `main/artifacts/smoke-968a1ec6521c489e9da80b85dbff6c87`. OS picker và thao tác hiệu chỉnh/file picker chưa được tự động hóa trong smoke này.

## Việc còn lại để chốt Phase 05 và chuyển Phase 06

1. Thu ảnh/replay ZZZ 1080p và 1440p ở từng UI scale cần hỗ trợ, gồm negative/icon tương tự, animation, blur, che khuất, menu/cutscene và character switch. Gán nhãn event, ghi rõ nguồn/version và quyền sử dụng asset.
2. Hiệu chỉnh ClientArea/ROI, thresholds và N trên tập train/calibration; giữ tập validation độc lập. Kiểm tra portrait nhân vật thật có đủ khác biệt cho matching Gray8 hay cần màu/NCC/search/matcher khác.
3. Kiểm tra picker window và monitor thủ công; resize, alt-tab, minimize/restore, mất game, màn hình mixed-DPI/HDR. WGC smoke chỉ kiểm tra cửa sổ thử, chưa kiểm chứng monitor/HDR/game.
4. Đo FP/FN/latency và CPU/GPU/RAM khi gameplay dài, độ trễ manual khi detector chậm; kiểm tra đổi preset/pause/đóng app trong lúc capture/recording. Không đóng cổng chất lượng chỉ dựa trên fixture tổng hợp.
5. Mục tiêu tiếp theo: giảm full-frame allocation/readback nếu profiling yêu cầu, cải thiện chọn ROI trực tiếp, cấu hình FPS/threshold dễ hơn và hỗ trợ matcher phù hợp dataset. Chính sách buff off-field/toàn đội vẫn cần đặc tả game.

Mã bắt đầu đọc: `Detection/FrameModels.cs`, `TemplateDetector.cs`, `CaptureEventSource.cs`, `ReplayDataset.cs`, `ReplayEvaluation.cs`; adapter Windows ở `App/WindowsCaptureSource.cs`, công cụ ở `CaptureWindow.xaml(.cs)`. `FakeGameEventSource` giữ lại cho regression test. Không cần làm lại Phase 01–04.

## Nguồn API

- [Microsoft: CreateFreeThreaded](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded?view=winrt-26100) — frame pool không phụ thuộc DispatcherQueue.
- [Microsoft: SoftwareBitmap.CreateCopyFromSurfaceAsync](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.imaging.softwarebitmap.createcopyfromsurfaceasync?view=winrt-26100) — CPU copy từ Direct3D surface.
- [Microsoft: D3D11CreateDevice](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-d3d11createdevice) — tạo device/context.
- [Microsoft: IGraphicsCaptureItemInterop::CreateForWindow](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow) — dùng trong smoke với HWND của cửa sổ thử; UI bình thường dùng OS picker.
