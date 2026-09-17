# Phase 03 — Buff State, Rule Engine và bàn giao

## Trạng thái

Đã hoàn thành Phase 03 trong Domain/Application và tích hợp vào worker hiện có. Build sạch, 33/33 test pass, benchmark Release và smoke WPF đều pass. Không có capture hoặc dữ liệu buff ZZZ thật ở phase này.

## File và trách nhiệm

| File | Trách nhiệm |
| --- | --- |
| `main/ZZZBuffTracker.Domain/Models.cs` | BuffDefinition/Rule/State, action, retrigger/loss policies, GameEvent và snapshot diagnostics |
| `main/ZZZBuffTracker.Domain/PresetRules.cs` | Validate preset/rule, tạo bộ rule tương thích preset Phase 01–02 |
| `main/ZZZBuffTracker.Domain/BuffTransitions.cs` | Hàm chuyển trạng thái thuần, không clock hệ thống, UI hay Windows API |
| `main/ZZZBuffTracker.Application/BuffRuleEngine.cs` | Xử lý event tuần tự, confidence, dedup, ordering, expiry projection, diagnostics |
| `main/ZZZBuffTracker.Application/TrackingService.cs` | Lifecycle, bounded channel, một worker sở hữu engine; Start/Stop/SwitchPreset/Dispose |
| `main/ZZZBuffTracker.Tests/RuleEngineTests.cs` | Regression nghiệp vụ và benchmark có TestCategory=Benchmark |
| `main/ZZZBuffTracker.Tests/TrackingTests.cs` | Lifecycle/concurrency và đổi preset với event cũ còn trong queue |
| `main/ZZZBuffTracker.App/DashboardViewModel.cs` | Điều khiển giả lập và hiển thị số event bị reject/quyết định cuối |

## Quy ước nghiệp vụ đã chốt

### Rule và action

`BuffRule` khớp chính xác `(GameEventType, SubjectId)`, xác định `BuffId`, `Action`, `MinimumConfidence` và `Cooldown`. Một event có thể tác động nhiều buff; không cho phép hai rule cùng trigger/subject tác động cùng buff để tránh thứ tự hành động mơ hồ.

Nếu `CharacterPreset.Rules` chưa được cung cấp (`IsDefault`), tạo các rule cơ bản cho từng buff. Nếu chủ động truyền `[]`, không tự tạo rule. Mỗi buff vẫn có thể được reset bằng global Reset.

| Action | Hành vi |
| --- | --- |
| Activate | Bắt đầu với một stack; nếu buff còn hiệu lực thì theo RetriggerMode |
| Refresh | Chỉ làm mới buff Active/Unknown còn stack; không kích hoạt Inactive/Pending/Expired và không tăng stack |
| Stack | Kích hoạt stack đầu nếu chưa active; cộng tới MaxStacks; `StackRefreshesDuration` quyết định có làm mới timer |
| Deactivate / Reset | Inactive, stack 0, bỏ deadline |
| Expire | Expired, stack 0; dùng được từ rule hoặc sự kiện thử thủ công |
| SetPending | Pending nếu chưa có buff Active/Unknown còn stack; không hạ Active về Pending |
| SignalLost | Theo bảng policy bên dưới; confidence thấp không làm thay đổi state |

`RetriggerMode`: Refresh (mặc định), Stack, Ignore. Duration vẫn là thời gian xác định dương, tối đa một ngày. Chưa hỗ trợ buff vô hạn hoặc timer riêng cho từng stack.

### Chính sách mất tín hiệu

| Policy | SignalLost đủ confidence | Đến deadline | BuffIconDisappeared đã xác nhận |
| --- | --- | --- | --- |
| ExpireByTimer | Giữ state/timer hiện tại | Expired, stack 0 | Deactivate |
| WaitForDisappear | Unknown, giữ stack | Active → Unknown; không tự deactivate | Deactivate |
| KeepUnknown | Unknown, giữ stack | Nếu đã Unknown thì giữ; nếu vẫn Active bình thường thì Expired | Deactivate |

Unknown/Pending không xuất countdown/progress giả; snapshot có remaining/progress = 0, presentation hiển thị `?`. Xác nhận bằng Activate/Refresh hợp lệ có thể đưa Unknown còn stack về Active. Chính sách không thay detector debounce: Phase 05 phải phát Disappeared chỉ sau xác nhận nhiều frame.

### Thời gian, late event và ordering

- Duration chỉ lấy từ `IClock.Elapsed` monotonic. Wall-clock chỉ dành cho log, không ảnh hưởng countdown.
- Deadline dựa trên `CapturedAt + Duration`, không dựa trên thời gian worker xử lý. Reject timestamp âm, tương lai hoặc event có tuổi **>= 30 giây**.
- Engine giữ state từ event và tạo state tại thời điểm hiển thị bằng hàm `Advance`. Snapshot/tick không phá dữ kiện event gốc. Ví dụ buff bắt đầu tại t=0, duration 6; UI tại t=8 đã thấy Expired, nhưng refresh capture t=5 vừa đến thì deadline đúng là t=11, còn 3 giây. Không cấp lại 6 giây tính từ t=8.
- Mỗi buff có watermark timestamp của event hợp lệ mới nhất. Event cũ hơn watermark bị loại; timestamp bằng nhau được xử lý theo thứ tự nhận. Nguồn cần bảo toàn thứ tự các sự kiện cùng timestamp; chưa có sequence number nguồn.
- Global Reset dùng `Type=Reset`, `SubjectId="*"`, confidence >= 0.8. Reset cũ hơn một buff đã cập nhật bị reject. Sau Reset, event có capture time **<= thời điểm reset** bị loại, kể cả event mới gửi nhưng vẫn mang timestamp cũ.
- Reset giữ dedup history. Reset vẫn được phép khi bộ nhớ dedup đầy để manual fallback không bị khóa.
- `SwitchPresetAsync` hủy và chờ worker/source cũ dừng, tạo engine/session mới và version mới. Event sai session hoặc preset version không được áp dụng. Phase 04 sẽ nối CharacterChanged với repository và API này.

### Idempotency và giới hạn bộ nhớ

- EventId chống lặp cho toàn engine/session trong cửa sổ lưu nhớ 30 giây.
- Correlation cooldown scope là `(RuleId, Source, CorrelationKey)`, so chênh lệch **capture time**. Cùng key có thể được chấp nhận lại sau cooldown. Rule icon mặc định 150 ms; rule manual mặc định 0. Hai event cùng EventId vẫn bị dedup dù cooldown=0.
- Nguồn phải dùng correlation ổn định cho cùng tín hiệu; sinh key mới cho mọi frame sẽ không tạo dedup theo correlation. Detector Phase 05 vẫn phải tạo edge event, không gửi refresh cho mọi frame icon tồn tại.
- Tối đa 4096 EventId và 4096 correlation key mỗi engine mặc định (`RuleEngineOptions.DedupCapacity` có thể cấu hình khi tạo engine). Khi đầy, reject rõ `CapacityExceeded`; không tự loại entry còn hiệu lực làm mất bảo đảm dedup. Prune theo TTL ngay khi xử lý event hoặc lấy snapshot.
- Sau TTL, replay giữ timestamp gốc bị `TooOld`; EventId tái sử dụng với timestamp hoàn toàn mới ngoài cửa sổ không được bảo đảm chống lặp vĩnh viễn.
- Rule cooldown chỉ cho phép 0–30 giây để nhất quán với cửa sổ lưu nhớ. Nguồn metadata chưa tin cậy cần thêm giới hạn kích thước payload ở bước tích hợp capture/import, trước khi enqueue.

### Concurrency và diagnostics

`BuffRuleEngine` không thread-safe có chủ ý: chỉ worker của `TrackingService` gọi Process/Snapshot. UI đọc snapshot bất biến qua store. Channel event capacity 128, backpressure; không drop semantic event. Start/Stop/SwitchPreset/Dispose dùng chung semaphore, không tạo hai reader/worker song song.

`ProcessedEvents` đếm event đã chấp nhận, kể cả xác nhận không làm thay đổi state (ví dụ Activate với Ignore). `ChangedBuffs` trong kết quả/log phân biệt trường hợp đó. `RejectedEvents` và `LastDecision` được hiển thị trong Control Panel. Event nhiều rule được chấp nhận nếu ít nhất một rule hợp lệ; các rule confidence thấp/cooldown/stale không được áp dụng.

Log ghi sessionId, eventId, correlation, quyết định và monotonic ProcessedAt. Giới hạn: log hiện ghi đồng bộ theo event, benchmark không bao gồm I/O log; KPI end-to-end sẽ đo ở Phase 06.

## Cách thử trên UI

Từ thư mục `main`:

```powershell
dotnet run --project .\ZZZBuffTracker.App\ZZZBuffTracker.App.csproj
```

1. Bấm Bắt đầu; tắt **Mẫu 3 buff** nếu đang bật để nhìn state thực của pipeline.
2. Mở **Thử Rule Engine (dữ liệu giả lập)**.
3. Pending → Kích hoạt → Stack +1 nhiều lần: stack tối đa 3.
4. Refresh giữ stack và làm mới timer 6 giây.
5. Mất tín hiệu → Unknown; demo dùng KeepUnknown. Refresh/Activate xác nhận lại; Deactivate đưa về Inactive.
6. Expire đưa về Expired; Reset xóa state. Khi không mất tín hiệu, timer vẫn tự Expired sau 6 giây.

Các nút chỉ phát GameEvent qua nguồn giả lập. UI không tự sửa BuffState. Chế độ **Mẫu 3 buff** của Phase 02 vẫn chỉ dùng snapshot tĩnh, độc lập với Rule Engine.

## Kiểm thử và benchmark

```powershell
# Trong main
dotnet build ZZZBuffTracker.sln --no-restore
dotnet test ZZZBuffTracker.sln --no-build --no-restore
dotnet test ZZZBuffTracker.Tests/ZZZBuffTracker.Tests.csproj -c Release --no-restore --filter TestCategory=Benchmark --logger "console;verbosity=detailed"
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Smoke-Test.ps1
```

Ngày 16/09/2026, Windows x64 / SDK 10.0.401:

- Build: 0 warning/error.
- 33/33 test pass. Bao gồm activate/refresh/stack cap/reset/deactivate/expire, Pending, các policy Unknown, dedup/cooldown/capacity/prune, custom rule confidence/subject, late timestamp, clock wall-time jumps, deterministic replay, validation, preset switch và regression Phase 01–02.
- Benchmark Release: 100.000 event, **652,26 ms**, khoảng **153.314 event/giây**, khoảng **653,2 byte cấp phát/event**, cuối lượt có 3.000 dedup entries.
- Benchmark tạo sẵn input, clock giả tăng 10 ms/event, lấy snapshot mỗi 100 event, một buff stackable. Có đo chi phí xử lý và projection, không đo source/channel/file log/WPF/capture. Đây là một lần đo trên môi trường dev, không phải cam kết hiệu năng máy khác hoặc latency end-to-end. Không đặt assertion về thời gian để tránh test chập chờn theo tải máy.
- Smoke WPF/Win32 **pass**, gồm các nút Pending → Activate → Stack 2/3 → Unknown → Refresh giữ stack → Deactivate → Expire, cùng regression overlay/settings/hotkey/focus/geometry/shutdown. Hai lần mở/đóng đều exit code 0. Artifact: `main/artifacts/smoke-fc05578530064530b2f8d0bac4841ea8` (được gitignore).

## Bàn giao để tiếp tục ở lượt sau

Cập nhật 17/09/2026: các đầu việc Phase 04 bên dưới đã được triển khai; xem [bàn giao Phase 04](10-Phase-04-Persistence-Preset-Hotkey.md) cho schema và điểm tiếp tục Phase 05. Danh sách sau giữ lại ngữ cảnh tại thời điểm chốt Phase 03.

Không cần làm lại Phase 01–03. Đọc `docs/07` để biết tiến độ chung, tài liệu này để nắm semantics, và `docs/04-Persistence-Preset-Hotkey.md` cho phase kế tiếp.

Các điểm đầu vào Phase 04:

1. Implement `IPresetRepository` và schema versioned cho preset/rule; giữ ý nghĩa khác nhau giữa Rules chưa cung cấp và Rules rỗng khi migration. Schema hiện tại của overlay chỉ áp dụng cho `overlay.json`, chưa phải schema profile tổng thể.
2. CRUD/import/export preset qua Application service; validate bằng `PresetRules.Validate` trước khi gọi `SwitchPresetAsync`.
3. Nối `CharacterChanged` với mapping/repository; hiện engine chỉ xử lý rule buff, không tự tra nhân vật. Không có preset cần trạng thái NotConfigured theo kế hoạch.
4. Chốt scope/owner cho buff toàn đội/off-field trước khi hỗ trợ; hiện switch reset toàn bộ state session. Không ngầm coi chính sách này đúng cho mọi Drive Disc thật.
5. Hotkey overlay/reset hiện đã có; Phase 04 bổ sung mapping cấu hình, trigger manual và kiểm tra conflict phù hợp.
6. Hoàn thành checklist DPI 100%/150% và input trong game của Phase 02 khi có môi trường phù hợp. Chưa có dataset/template để tuyên bố auto detection.

Giới hạn còn lại: timer riêng từng stack, buff vô hạn, history replay ngoài 30 giây, rule condition tùy ý và event sequence number chưa thuộc implementation hiện tại. Nếu cần các tính năng này phải mở rộng model cùng test thay vì viết logic vào UI.
