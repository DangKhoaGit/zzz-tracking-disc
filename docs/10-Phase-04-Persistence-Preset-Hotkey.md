# Phase 04 — Persistence, Preset và Hotkey

Ngày triển khai: 17/09/2026. Phase 04 nối preset bền vững với Rule Engine và Control Panel; vẫn dùng nguồn giả lập, chưa capture game.

## Đã triển khai

- `ProfileDocument` schema 2 chứa Character, DriveDiscSet, preset với BuffDefinition/BuffRule và OverlaySettings. Tất cả dữ liệu sample là minh họa, không phải dữ liệu Drive Disc đã xác minh.
- `JsonProfileRepository` implement `IPresetRepository` và `IProfileRepository`; validate tham chiếu nhân vật/bộ đĩa, buff/rule, enum và overlay trước khi ghi/import.
- Ghi file tạm cùng thư mục, flush xuống đĩa rồi atomic replace; bản trước là `.bak`. File lock và revision check ngăn hai instance ghi đè thay đổi của nhau. Nếu file bị khóa hoặc revision đã đổi, báo lỗi để nạp lại.
- Primary hỏng và backup hợp lệ: archive primary thành `.corrupt-<id>`, restore backup; có log. Không có backup hợp lệ thì báo lỗi, không reset config âm thầm. Schema tương lai bị từ chối, không downgrade qua backup.
- Migration schema 1 → 2 và migration `overlay.json` Phase 02/03 vào profile chung khi chưa có profile. File overlay cũ được giữ nguyên.
- Cửa sổ **Presets / Profiles**: tạo/chọn/sửa/xóa preset, sửa tên nhân vật, thêm/bỏ/sửa nhiều buff qua DataGrid, dùng nhân vật đã chọn, giả lập CharacterChanged, import preview/confirm và export.
- `CharacterChanged` đủ confidence đi qua Application worker → repository → session/engine/source inbox mới. Không có preset: `NotConfigured`, xóa snapshot cũ. Có thể phục hồi bằng đổi sang nhân vật có preset hoặc lưu preset rồi áp dụng lại.
- Pause detection bỏ qua event tự động, giữ timer và manual hoạt động. Manual trigger/refresh/reset qua Application service, không sửa state trong UI.
- Sáu global hotkey cố định, báo conflict và unregister lúc shutdown; không hook process/game/bàn phím.

## Cách dùng

Từ `main`, chạy:

```powershell
dotnet run --project .\ZZZBuffTracker.App\ZZZBuffTracker.App.csproj
```

1. Mở **Presets / Profiles** từ Dashboard.
2. Chọn preset có sẵn để sửa, hoặc **Mới** rồi nhập Character ID, tên và các dòng buff. ID mới trùng preset có sẵn sẽ bị từ chối; muốn sửa phải chọn preset đó.
3. **Lưu preset** tăng version. Nếu đang tracking chính nhân vật này, session/state được tạo lại với cấu hình mới.
4. **Dùng nhân vật này** áp dụng ngay ID trong ô Character ID, kể cả ID chưa có preset để thử NotConfigured.
5. **Giả lập CharacterChanged** gửi event nhận diện; cần Start trước, detection đang bật và không làm việc trên state tĩnh **Mẫu 3 buff** để quan sát kết quả. Hiện nguồn fake luôn gửi confidence=1; worker yêu cầu >=0.9 cho character switch.
6. **Xóa** có xác nhận. Chỉ xóa preset, giữ metadata nhân vật; nếu đó là nhân vật đang chạy, chuyển NotConfigured.
7. Chọn một dòng buff trong bảng để hotkey/manual nhắm vào buff đó nếu thuộc session hiện tại; nếu không, nhắm vào buff đầu tiên của session.

Buff ID đổi được nhưng được xem là bỏ buff cũ và thêm buff mới: rule của ID cũ bị bỏ, ID mới nhận rule mặc định. Rule custom đã import cho các ID không đổi được giữ nguyên. UI hiện chỉnh thông số buff, chưa có trình thiết kế điều kiện rule; chỉnh custom rules và danh mục DriveDiscSet trong file profile rồi import có validation. Disc ID để trống là hợp lệ, nếu nhập phải tham chiếu disc đã khai báo.

### Import/export

- **Chọn file import** chỉ đọc, migrate và validate, không sửa dữ liệu đang dùng. Hiển thị số nhân vật/preset/bộ đĩa và danh sách preset/buff count.
- **Áp dụng import** có hộp xác nhận thay toàn bộ profile. Revision đổi từ lúc preview thì từ chối, phải preview lại sau Nạp lại. Không merge âm thầm.
- Cấu hình hiển thị trong import được áp dụng; UI giữ geometry của màn hình hiện tại và lưu geometry đó vào profile để không di chuyển overlay sang vị trí máy khác.
- **Export profile** xuất cấu hình đang dùng cùng overlay đang chỉnh. File đã tồn tại phải xác nhận trong save dialog; bản trước được backup. Không export đè vào file profile đang chạy hoặc `.bak` của nó.
- File đọc/ghi giới hạn 2 MB. Lỗi JSON, field thiếu, tham chiếu sai hoặc rule không hợp lệ được hiển thị để sửa. Field không nhận biết bị từ chối để tránh mất dữ liệu do serialize lại.

Sample đã được unit test:

- [profile-v2.json](../main/samples/profile-v2.json): 3 nhân vật, 2 preset, 1 bộ đĩa demo; một nhân vật chưa có preset để thử mapping.
- [profile-v1.json](../main/samples/profile-v1.json): dữ liệu cũ tối thiểu để thử migration.

### Hotkey

| Phím | Tác dụng |
| --- | --- |
| Ctrl+Alt+O | Ẩn/hiện overlay |
| Ctrl+Alt+E | Edit hoặc Lưu & Play |
| Ctrl+Alt+R | Reset buff |
| Ctrl+Alt+T | Activate/retrigger buff được chọn |
| Ctrl+Alt+F | Refresh, không tăng stack |
| Ctrl+Alt+D | Bật/tạm dừng detection; manual vẫn dùng được |

Hotkey conflict được báo trong Control Panel; dùng nút tương ứng khi phím đã được ứng dụng khác chiếm. Phải khởi động lại sau khi giải phóng phím để đăng ký lại. Chưa có UI đổi tổ hợp phím.

## File cấu hình và schema

Nguồn dữ liệu chính từ Phase 04: `%LOCALAPPDATA%/ZZZBuffTracker/profiles.json`.

- `.bak`: phiên bản ngay trước lần ghi thành công gần nhất, không phải toàn bộ lịch sử.
- `.corrupt-<id>`: bản primary hỏng được giữ khi recovery.
- `.lock`: tên file dùng để lấy khóa exclusive. File có thể vẫn tồn tại khi app tắt; khóa hệ điều hành được nhả khi handle đóng, không được coi sự tồn tại file là dấu hiệu đang bị khóa.
- `logs/tracking.jsonl`: log save, backup, recovery, import validation, export và character switch.
- `overlay.json` chỉ còn là nguồn migration cũ; sau migration, thay đổi mới ghi vào `profiles.json`. Không sửa overlay.json để điều khiển Phase 04.

`SchemaVersion=2` của profile độc lập với `Overlay.SchemaVersion=1`, `Revision` toàn file và `CharacterPreset.Version`. Save preset tăng version preset; mọi save tăng revision file. Lưu overlay trong lúc đang mở một bản nháp preset làm revision của editor cũ đi; **Nạp lại** trước khi chỉnh/lưu lại nếu gặp thông báo conflict. Đây là bảo vệ chống ghi đè, không phải lỗi mất preset.

Schema 1 được hỗ trợ là object PascalCase có `Presets`, mỗi preset có `CharacterId`, `Buffs` và tùy chọn Version/Rules. Migration tạo Characters từ ID, mặc định disc catalog rỗng và overlay mặc định. Buff phải có ID, Name và Duration hợp lệ; các policy mới dùng default của model. Không tự suy luận format ví dụ cũ không có schema trong tài liệu ý tưởng.

Rules không cung cấp/null → sinh compatibility defaults; `Rules: []` → chủ động không có rule buff. Export/save chuẩn hóa thành mảng explicit để ổn định round-trip.

## Concurrency, lifecycle và giới hạn

Character switch hủy và chờ producer cũ, thay bounded queue, tạo source context mới rồi tiếp tục trên cùng worker. Event từ inbox cũ không được dùng cho nhân vật mới. Event quá cũ, sai session/version hoặc confidence thấp bị bỏ. Lặp cùng nhân vật không reset timer vô ích.

Switch/reset hiện áp dụng toàn session, không giữ buff off-field/toàn đội. Phải chốt scope/owner bằng dữ liệu game trước khi hỗ trợ những loại buff này. Pause detection hiện là lọc semantic event tự động; Phase 05 cần nối nó với capture/detector để giảm CPU/GPU thực tế.

CRUD/import/export profile chạy qua `ProfileService`, I/O qua repository; UI chỉ thực hiện file picker, xác nhận và chỉnh dữ liệu. Các thao tác hiện tại nhỏ và atomic; kiểm tra shutdown giữa capture/import tải nặng cùng profiling dài vẫn thuộc Phase 06.

## Kiểm chứng và điểm tiếp tục

```powershell
# Trong main
dotnet build ZZZBuffTracker.sln --no-restore
dotnet test ZZZBuffTracker.sln --no-build --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Smoke-Test.ps1
```

Đã kiểm tra build 0 warning/error và **43/43 test pass**: round-trip, sample v1/v2, missing/default fields, empty rules, backup/recovery, future schema, CRUD/version/revision conflict, import preview/export overwrite, lỗi ghi/cancellation, character mapping và manual trong pause; gồm regression Phase 01–03.

Smoke WPF/Win32 **pass**: create/update/apply preset qua cửa sổ thật, manual hotkeys khi detection pause, lưu/nạp profile/geometry, kiểm tra conflict bằng cách chiếm Ctrl+Alt+T trước khi chạy app, kiểm tra release đủ sáu hotkey sau shutdown và regression Rule Engine/overlay. Hai lượt mở/đóng đều exit code 0. Artifact: `main/artifacts/smoke-1fb29e6f69ad406da71d6c2e842a5ad4` (gitignored).

**Phase 04 hoàn thành theo phạm vi trên.** File picker/confirmation import-export/delete có trong UI; logic tương ứng được kiểm chứng qua unit/integration test. Smoke tự động chưa nhấn mọi hộp thoại file picker hoặc kiểm thử input trong game. Checklist DPI/game của Phase 02 vẫn chưa được thay thế bằng các kết quả này.

Phase 05 bắt đầu từ `IGameEventSource` và `TrackingContext`: source nhận session/preset version; phải dừng sạch khi character/preset switch và tạo frame/ROI/template metadata trước khi phát GameEvent. `IsManual=false` cho detector, chỉ các thao tác manual mới đặt true. Không sửa BuffState trong Detection. Cần dataset 1080p/1440p, ROI/template version và capture spike; chưa có nhận diện game thật trong mã Phase 04.
