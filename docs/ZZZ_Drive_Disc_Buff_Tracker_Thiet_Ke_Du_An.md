ZZZ DRIVE DISC BUFF TRACKER

Tài liệu tổng hợp ý tưởng, kiến trúc và đặc tả dự án

Desktop Companion Overlay cho Zenless Zone Zero

Mục tiêu: hiển thị và theo dõi trạng thái buff của Drive Disc ngay trên
màn hình game, cho phép hiển thị dạng chữ hoặc hình ảnh, tự động chọn
cấu hình đĩa theo nhân vật và cung cấp một cửa sổ quản lý riêng bên
ngoài game.

# 1. Tổng quan dự án

ZZZ Drive Disc Buff Tracker là ứng dụng desktop companion chạy song song
với Zenless Zone Zero (ZZZ). Ứng dụng không cần sửa giao diện game. Một
cửa sổ overlay trong suốt, luôn nằm trên game, hiển thị các Drive
Disc/Drive Disc Set đang được theo dõi và trạng thái buff tương ứng.
Người dùng có thể chọn cách hiển thị bằng text hoặc icon/hình ảnh.

Ứng dụng có thêm một cửa sổ Control Panel độc lập dùng để cấu hình nhân
vật, bộ đĩa, vị trí overlay, kiểu hiển thị, hotkey và cơ chế nhận diện.
Khi đổi nhân vật, hệ thống có thể tự động nạp preset Drive Disc đã lưu
cho nhân vật đó.

# 2. Phạm vi và nguyên tắc kỹ thuật

- Ứng dụng chạy độc lập với game; ưu tiên screen capture, computer
  vision và dữ liệu cấu hình.

- Không đọc/ghi bộ nhớ game, không inject DLL, không hook hoặc chỉnh
  sửa process của ZZZ.

- Overlay chỉ phục vụ hiển thị và theo dõi trạng thái do ứng dụng suy
  luận/nhận diện.

- Thiết kế Detection Layer độc lập để sau này có thể thay đổi cách
  nhận diện mà không ảnh hưởng UI và nghiệp vụ.

- Có chế độ Manual/Hotkey làm phương án dự phòng khi nhận diện tự động
  không đủ tin cậy.

# 3. Công nghệ đề xuất

| Thành phần \| Công nghệ \| Vai trò \|

| --- \| --- \| --- \|

| Ngôn ngữ chính \| C# / .NET 8+ \| Logic ứng dụng, Windows API, cấu
hình và xử lý sự kiện \|

| Desktop UI \| WPF \| Control Panel và cửa sổ Overlay \|

| Kiến trúc UI \| MVVM \| Tách View, ViewModel và nghiệp vụ \|

| Computer Vision \| OpenCvSharp \| Template matching, crop ROI, nhận
diện icon/UI \|

| Capture \| Windows Graphics Capture \| Lấy frame game hiệu quả \|

| Lưu cấu hình \| JSON \| Preset nhân vật, Drive Disc, buff rule, UI
settings \|

| Logging \| Serilog hoặc logging .NET \| Theo dõi lỗi, detection
confidence, trạng thái runtime \|

| Testing \| xUnit/NUnit \| Unit test cho rule engine, timer, preset và
state manager \|

# 4. Kiến trúc tổng thể

Kiến trúc nên kết hợp MVVM ở tầng giao diện với kiến trúc phân lớp cho
phần nghiệp vụ. Các tầng không phụ thuộc trực tiếp vào game; dữ liệu từ
game đi vào hệ thống thông qua Detection Layer.

ZZZ → Capture → Detection → GameEvent → Buff Rule Engine → Buff State
Manager → Overlay ViewModel → Overlay

| Tầng/Module \| Trách nhiệm chính \|

| --- \| --- \|

| Presentation \| Control Panel, Overlay, Settings UI, Preview \|

| Application \| Điều phối use case: chọn nhân vật, load preset,
start/stop tracking, cập nhật overlay \|

| Domain \| Character, DriveDiscSet, BuffRule, BuffState, GameEvent và
các quy tắc nghiệp vụ \|

| Detection \| Capture frame, ROI, template matching, nhận diện nhân
vật/buff/event \|

| Infrastructure \| JSON repository, file system, hotkey, Windows API,
logging \|

# 5. Cấu trúc solution đề xuất

```text
ZZZBuffTracker.sln
│
├── ZZZBuffTracker.App
│   ├── Views
│   │   ├── MainWindow.xaml
│   │   ├── OverlayWindow.xaml
│   │   ├── CharacterPage.xaml
│   │   ├── DiscPage.xaml
│   │   └── SettingsPage.xaml
│   └── ViewModels
│
├── ZZZBuffTracker.Application
│   ├── Services
│   ├── UseCases
│   └── DTOs
│
├── ZZZBuffTracker.Domain
│   ├── Entities
│   ├── Enums
│   ├── Events
│   └── Rules
│
├── ZZZBuffTracker.Detection
│   ├── Capture
│   ├── Detectors
│   ├── Templates
│   └── ROI
│
├── ZZZBuffTracker.Infrastructure
│   ├── Persistence
│   ├── Hotkeys
│   ├── Windows
│   └── Logging
│
└── ZZZBuffTracker.Tests
```

# 6. Mô hình dữ liệu chính

| Entity \| Thuộc tính gợi ý \| Ý nghĩa \|

| --- \| --- \| --- \|

| Character \| Id, Name, IconPath, DetectionTemplates \| Nhân vật ZZZ \|

| CharacterPreset \| CharacterId, DiscSetIds, DisplayMode, Rules \|
Preset tự động cho từng nhân vật \|

| DriveDiscSet \| Id, Name, IconPath, Description \| Thông tin bộ Drive
Disc \|

| BuffDefinition \| Id, Name, Duration, MaxStacks, RefreshMode \| Định
nghĩa buff \|

| BuffRule \| TriggerType, Conditions, BuffId \| Điều kiện kích hoạt
buff \|

| BuffState \| IsActive, ActivatedAt, Remaining, Stack \| Trạng thái
runtime \|

| GameEvent \| Type, Timestamp, Confidence, Metadata \| Sự kiện
Detection Layer tạo ra \|

| OverlaySettings \| Position, Scale, Opacity, DisplayMode \| Cấu hình
overlay \|

# 7. Nghiệp vụ cốt lõi

## 7.1. Quản lý nhân vật và bộ đĩa

- Người dùng có thể tạo/lưu preset cho từng nhân vật.

- Một preset chứa các Drive Disc Set cần tracking và rule tương ứng.

- Có thể sửa preset thủ công từ Control Panel.

- Khi nhân vật hiện tại thay đổi, ứng dụng tự động tìm preset và cập
  nhật overlay.

## 7.2. Tự động setting Drive Disc theo nhân vật

Có hai mức tự động. Mức ổn định nhất là Auto Preset: ứng dụng nhận diện
nhân vật đang active rồi nạp preset mà người dùng đã cấu hình trước. Mức
nâng cao là nhận diện trực tiếp thông tin build từ các màn hình UI phù
hợp nếu hình ảnh trong game cung cấp đủ dữ liệu.

1.  Detection Layer nhận diện CharacterChanged(characterId).

2.  Application tìm CharacterPreset tương ứng.

3.  Nếu có preset: nạp danh sách Drive Disc/BuffRule.

4.  Buff State Manager reset các state không còn hợp lệ.

5.  Overlay cập nhật ngay theo preset mới.

6.  Nếu không có preset: hiển thị trạng thái 'Chưa cấu hình' và cho phép
    người dùng chọn preset.

## 7.3. Tracking buff

Detection Layer không trực tiếp điều khiển overlay. Nó chỉ phát sinh
GameEvent. Buff Rule Engine nhận event và quyết định buff nào được
Activate, Refresh, Stack hoặc Deactivate.

| Trạng thái \| Ý nghĩa \|

| --- \| --- \|

| Inactive \| Buff chưa được kích hoạt \|

| Active \| Buff đang có hiệu lực \|

| Refresh \| Trigger mới làm mới thời gian \|

| Stack \| Buff tăng số tầng \|

| Expired \| Hết thời gian hiệu lực \|

## 7.4. Hai chế độ hiển thị Drive Disc

Người dùng có thể đổi DisplayMode trong Control Panel mà không cần
restart ứng dụng.

| Mode \| Ví dụ \| Phù hợp \|

| --- \| --- \| --- \|

| Text \| Woodpecker Electro \| 4.8s \| Stack 2 \| Tối giản, dễ đọc, ít
chiếm màn hình \|

| Image/Icon \| \[Icon\] 4.8s + progress bar \| Trực quan, gần UI game
\|

| Hybrid - tùy chọn \| \[Icon\] Woodpecker \| 4.8s \| Kết hợp hình và
chữ \|

# 8. Overlay Window

- Transparent background.

- Always-on-top.

- Không xuất hiện trong taskbar nếu người dùng chọn.

- Click-through trong Play Mode để không cản thao tác game.

- Edit Mode cho phép kéo, resize và thay đổi vị trí.

- Điều chỉnh opacity, scale, spacing, orientation dọc/ngang.

- Ẩn buff inactive hoặc hiển thị mờ tùy cấu hình.

- Progress bar/countdown cho buff có thời lượng.

- Hiển thị stack với buff nhiều tầng.

# 9. Control Panel bên ngoài game

Control Panel là cửa sổ quản lý chính. Nó có thể mở độc lập trong khi
Overlay vẫn chạy.

| Khu vực \| Chức năng \|

| --- \| --- \|

| Dashboard \| Trạng thái ZZZ, tracking on/off, nhân vật hiện tại,
preset đang dùng \|

| Characters \| Danh sách nhân vật và mapping preset \|

| Drive Discs \| Danh sách bộ đĩa, icon, buff definition và rule \|

| Overlay \| Text/Image/Hybrid, scale, opacity, vị trí, preview \|

| Detection \| Chọn monitor/window, ROI, threshold/confidence, test
detection \|

| Hotkeys \| Toggle overlay, Edit Mode, reset buff, trigger thủ công \|

| Profiles \| Import/export preset và backup cấu hình \|

| Diagnostics \| FPS capture, detection confidence, event log \|

# 10. Detection Layer

## 10.1. Screen Capture

Ưu tiên Windows Graphics Capture và chỉ xử lý các Region of Interest
(ROI) cần thiết thay vì toàn màn hình. Capture có thể chạy khoảng 10-20
FPS cho detection, trong khi overlay vẫn render mượt độc lập.

## 10.2. Nhận diện

- Template Matching cho icon hoặc thành phần UI có hình dạng ổn định.

- Scale-normalized ROI để hỗ trợ 1080p/1440p và các tỉ lệ UI khác
  nhau.

- Confidence threshold để tránh false positive.

- Debounce/cooldown detection để một icon tồn tại nhiều frame không
  tạo hàng loạt event.

- Có thể bổ sung OCR sau này nếu cần đọc số/chữ; không nên là phụ
  thuộc chính của MVP.

## 10.3. Các GameEvent gợi ý

| Event \| Nguồn \| Ứng dụng \|

| --- \| --- \| --- \|

| CharacterChanged \| Nhận diện portrait/UI \| Auto load preset \|

| BuffIconAppeared \| Template matching \| Activate buff \|

| BuffIconDisappeared \| Template matching \| Deactivate/verify
expiration \|

| CombatStarted \| UI detection \| Reset/init runtime state \|

| CombatEnded \| UI detection \| Clear hoặc pause tracking \|

| ManualTrigger \| Global hotkey \| Fallback/debug \|

# 11. Buff Rule Engine

Rule Engine là trung tâm nghiệp vụ. Mỗi Drive Disc không nên được
hard-code trực tiếp trong UI. Rule nhận GameEvent và trả về hành động
đối với BuffState.

```text
Ví dụ logic khái niệm:

IF event = BuffIconAppeared(WOODPECKER)
AND confidence >= configuredThreshold
THEN Activate(WOODPECKER_BUFF, duration)

IF buff đang Active AND refreshMode = Refresh
THEN ActivatedAt = now

IF stackable AND stack < maxStacks
THEN stack = stack + 1
```

# 12. Luồng hoạt động chính

1.  Người dùng mở ZZZ Buff Tracker.

2.  Ứng dụng tìm cửa sổ ZZZ và khởi tạo capture.

3.  Control Panel load settings và preset.

4.  Detection nhận diện nhân vật hiện tại.

5.  Preset Drive Disc của nhân vật được tự động nạp.

6.  Overlay dựng danh sách buff theo Text/Image/Hybrid.

7.  Trong gameplay, Detection tạo GameEvent.

8.  Rule Engine xử lý event và cập nhật BuffState.

9.  Overlay ViewModel nhận state mới và cập nhật timer/progress bar.

10. Khi đổi nhân vật, preset và overlay được chuyển tự động.

# 13. Các chức năng của hệ thống

| Nhóm \| Chức năng \|

| --- \| --- \|

| Overlay \| Bật/tắt, Text/Image/Hybrid, timer, progress, stack,
kéo/thay đổi kích thước \|

| Character \| Thêm preset, sửa preset, auto switch preset \|

| Drive Disc \| Quản lý set, icon, buff, duration, trigger,
stack/refresh \|

| Detection \| Capture game, ROI, template matching, confidence \|

| Tracking \| Activate, refresh, stack, expire, reset \|

| Control Panel \| Preview overlay, cấu hình UI, cấu hình detection \|

| Hotkey \| Toggle overlay, edit mode, reset, manual trigger \|

| Data \| Save/load JSON, import/export profile \|

| Diagnostics \| Event log, detection confidence, capture FPS \|

# 14. Cấu trúc dữ liệu JSON minh họa

```text
{
  "characterId": "character_x",
  "displayMode": "Image",
  "discSets": [
    {
      "discSetId": "woodpecker_electro",
      "enabled": true,
      "buff": {
        "durationSeconds": 6,
        "maxStacks": 1,
        "refreshMode": "Refresh"
      }
    }
  ],
  "overlay": {
    "scale": 1.0,
    "opacity": 0.9,
    "orientation": "Vertical"
  }
}
```

# 15. Phi chức năng

- Overlay không làm mất focus của game.

- CPU/GPU overhead thấp; chỉ capture ROI cần thiết.

- Detection và UI chạy bất đồng bộ để tránh giật overlay.

- Ứng dụng chịu được việc ZZZ đóng/mở lại hoặc đổi resolution.

- Cấu hình tự lưu và có khả năng backup/import/export.

- Nếu Detection lỗi, tracker không crash; chuyển sang trạng thái không
  xác định hoặc manual.

- Logging đủ để debug false positive/false negative.

# 16. Concurrency và luồng xử lý

Nên tách Capture/Detection khỏi UI thread. Capture loop lấy frame,
detector xử lý frame và phát event qua event bus/channel. Buff State
Manager xử lý tuần tự các event để tránh race condition. WPF Dispatcher
chỉ được dùng ở bước cuối để cập nhật ViewModel/UI.

Capture Thread → Detection Worker → Event Channel → Buff State Manager →
Overlay ViewModel → WPF Dispatcher

# 17. MVP và lộ trình phát triển

| Giai đoạn \| Mục tiêu \|

| --- \| --- \|

| MVP 1 \| WPF Control Panel + transparent click-through overlay \|

| MVP 2 \| Text/Image display + BuffState + timer + progress \|

| MVP 3 \| Character preset + JSON + auto switch preset thủ công/hotkey
\|

| MVP 4 \| Windows Graphics Capture + ROI preview \|

| MVP 5 \| OpenCV template matching + GameEvent \|

| MVP 6 \| Auto character detection + auto preset \|

| MVP 7 \| Rule engine hoàn chỉnh, stack/refresh, diagnostics \|

| MVP 8 \| Profile sharing, import/export và tối ưu nhiều resolution \|

# 18. Kiểm thử

- Unit test BuffState: activate, expire, refresh và stack.

- Unit test Rule Engine với từng GameEvent.

- Unit test CharacterPreset mapping.

- Unit test JSON serialization/deserialization.

- Detection test bằng tập screenshot cố định ở nhiều resolution.

- Integration test: CharacterChanged → load preset → overlay state.

- Performance test capture/detection để theo dõi CPU, RAM và latency.

# 19. Rủi ro và hướng xử lý

| Rủi ro \| Hướng xử lý \|

| --- \| --- \|

| UI game thay đổi \| Template versioning và cho phép cập nhật
template/ROI \|

| Resolution khác nhau \| Normalize tọa độ ROI và scale template \|

| False positive \| Confidence + debounce + xác nhận nhiều frame \|

| False negative \| Nhiều template, threshold theo detector, manual
fallback \|

| Overlay che gameplay \| Opacity/scale/position + Edit/Play Mode \|

| Game mất focus \| Click-through và không activate overlay khi cập nhật
\|

| Game/anti-cheat \| Không inject/hook/read memory; companion app độc
lập \|

# 20. Đề xuất giao diện

Control Panel nên có thanh điều hướng bên trái và vùng preview bên phải.
Dashboard hiển thị trạng thái game, nhân vật được nhận diện, preset hiện
tại và nút Start/Stop Tracking. Trang Overlay cho phép chuyển
Text/Image/Hybrid và xem preview tức thời. Trang Detection nên có ảnh
frame/ROI để người dùng tự kiểm tra nhận diện.

Overlay trong game nên tối giản: icon/text của set, countdown, progress
bar và stack. Các thông tin cấu hình chi tiết chỉ xuất hiện trong
Control Panel, tránh làm màn hình game rối.

# 21. Kết luận kiến trúc

Cấu hình phù hợp nhất cho dự án là C#/.NET + WPF/MVVM, tách
Domain/Application/Detection/Infrastructure. Overlay và Control Panel là
hai cửa sổ của cùng ứng dụng, dùng chung state. CharacterPreset chịu
trách nhiệm tự động setting bộ đĩa theo nhân vật; Detection chỉ tạo
GameEvent; Buff Rule Engine quyết định trạng thái; Overlay chỉ hiển thị.
Cách tách này giúp dự án dễ mở rộng, dễ test và tránh phụ thuộc trực
tiếp vào process game.

# 22. Sơ đồ module cuối cùng

```text
┌──────────────────────────────┐
                     │      CONTROL PANEL           │
                     │ Character / Disc / Settings  │
                     └──────────────┬───────────────┘
                                    │
                                    ▼
┌─────────────┐   ┌─────────────┐   ┌───────────────────┐
│     ZZZ     │──▶│   Capture   │──▶│ Detection Layer   │
└─────────────┘   └─────────────┘   └─────────┬─────────┘
                                              │ GameEvent
                                              ▼
                                    ┌───────────────────┐
                                    │ Buff Rule Engine  │
                                    └─────────┬─────────┘
                                              ▼
                                    ┌───────────────────┐
                                    │ Buff State Manager│
                                    └─────────┬─────────┘
                                              │
                       ┌──────────────────────┴─────────────────────┐
                       ▼                                            ▼
              ┌─────────────────┐                         ┌─────────────────┐
              │ CharacterPreset │                         │ Overlay ViewModel│
              └─────────────────┘                         └────────┬────────┘
                                                                  ▼
                                                         ┌─────────────────┐
                                                         │ GAME OVERLAY    │
                                                         │ Text / Image    │
                                                         └─────────────────┘
```
