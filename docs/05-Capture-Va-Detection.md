# Phase 05 - Capture va Detection

Tiến độ triển khai 17/09/2026: xem [11 — Capture, Detection và Replay](11-Phase-05-Capture-Detection-Replay.md). Đã có pipeline/công cụ và kiểm thử WGC; dataset ZZZ và kiểm chứng độ chính xác thực tế còn mở.

## Muc tieu

Ket noi frame tu cua so ZZZ va tao GameEvent co confidence, co debounce, co ROI va co the debug bang replay.

## Pham vi

- Windows Graphics Capture cho window/monitor da chon.
- ROI normalized theo client area va scale UI.
- Capture worker bounded queue; bo frame cu khi detector cham.
- Template repository co version, resolution scale va asset validation.
- Template matching cho CharacterChanged va BuffIconAppeared/Disappeared.
- Multi-frame confirmation, cooldown, confidence theo detector.
- Event metadata: source, ROI, template version, captured time, confidence.
- ROI preview, test detection va luu replay dataset.

## Nguyen tac an toan

- Khong memory read, inject, hook hoac thay doi process/file cua game.
- Capture loi phai chuyen sang `CaptureUnavailable`, khong lam crash tracker.
- Khi mat cua so hoac doi resolution, detector pause/reconfigure va bao trang thai.

## Dau ra

- Fake frame va screenshot co the chay qua cung detector.
- Event khong trung do mot icon ton tai qua nhieu frame.
- Co log de giai thich vi sao event bi reject boi threshold/debounce.

## Kiem thu

- Screenshot dataset co version o 1080p, 1440p va UI scale khac nhau.
- False positive tu icon tuong tu.
- False negative khi icon bi che/blur.
- Resolution/window resize va mat capture.
- Do latency capture -> event va queue depth.

## DoD

- Detection chi phat event, khong sua BuffState.
- Auto character switch chay voi preset gia lap.
- Co manual fallback neu detector `Unknown` hoac `Unavailable`.

## Phu thuoc

Phase 01, 03 va 04. Khong can OCR trong phase nay.
