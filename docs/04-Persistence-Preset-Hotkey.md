# Phase 04 - Persistence, Preset va Hotkey

Triển khai, schema, cách dùng và kiểm chứng: [10 — Persistence, Preset và Hotkey](10-Phase-04-Persistence-Preset-Hotkey.md).

## Muc tieu

Cho phep nguoi dung cau hinh preset ben vung, chuyen preset theo nhan vat va dung Manual fallback.

## Pham vi

- JSON schema versioned cho Character, DriveDiscSet, BuffDefinition, BuffRule va OverlaySettings.
- Repository atomic write, backup truoc khi ghi va recovery khi JSON hong.
- CRUD preset trong Control Panel.
- Import/export profile co validation, khong ghi de im lang.
- Manual trigger cho activate/refresh/reset va bat/tat detection.
- Mapping CharacterChanged -> preset; khong co preset thi hien `NotConfigured`.
- Global hotkey dang ky/huy theo lifecycle, tranh trung phim.

## Dau ra

- Nap cau hinh cu khong lam mat field moi nhờ migration/default.
- Doi preset reset state dung session/version.
- Import file sai hien loi co the sua.
- Manual mode dung duoc khi detection khong tin cay.

## Kiem thu

- Round-trip serialize/deserialize.
- Migration tu schema cu.
- File hong, file thieu field, path khong ghi duoc.
- CharacterChanged co va khong co preset.
- Hotkey register conflict va shutdown.

## DoD

- Co file sample profile versioned.
- Backup/import/export duoc ghi log.
- Moi thao tac tu Control Panel di qua Application service.

## Phu thuoc

Phase 01 va Phase 03. Co the dung fake CharacterChanged truoc khi co detector that.
