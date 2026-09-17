# Phase 06 - Tich hop, toi uu va phat hanh

## Muc tieu

Xac nhan he thong chay on dinh trong workflow that, co kha nang chuyen che do, chan doan va phat hanh co the lap lai.

## Pham vi

- End-to-end: ZZZ window -> capture -> detection -> event -> rule -> snapshot -> overlay.
- CombatStarted/Ended, CharacterChanged va mat focus/cua so.
- Diagnostics view: capture FPS, detection FPS, latency, confidence, queue depth, event log.
- Replay test regression cho dataset da biet.
- Profiling CPU/RAM, frame drop va startup/shutdown.
- Tinh chinh threshold theo detector va profile resolution.
- Profile sharing, import/export va backup.
- Publish self-contained Windows build, config migration va rollback config.

## KPI de chot truoc phat hanh

- Event-to-overlay latency muc tieu duoi 150 ms trong test machine.
- Khong crash khi ZZZ dong/mo lai hoac mat capture.
- CPU/RAM nam trong gioi han da ghi nhan tren test machine.
- False positive/negative duoc do tren dataset version cu the.
- Manual fallback luon thao tac duoc khi auto detection that bai.

## Kiem thu

- Unit, integration va replay regression.
- Smoke test Play Mode/Edit Mode tren nhieu resolution va Windows scaling.
- Long-running test toi thieu mot session gameplay dai.
- Test upgrade tu schema config cu va backup/restore.
- Test tat ung dung khi capture, detection va import dang dang xu ly.

## DoD phat hanh

- Co release checklist, version app va version template/dataset.
- Co huong dan reset config va bat diagnostics.
- Khong co loi nghiem trong trong event pipeline, overlay focus hoac persistence.
- OCR/auto doc build chi duoc mo thanh phase sau neu KPI MVP da dat.

## Phu thuoc

Tat ca phase truoc.
