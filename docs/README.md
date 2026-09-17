# ZZZ Drive Disc Buff Tracker - Bo tai lieu

Tai lieu goc cua du an nam trong [ZZZ_Drive_Disc_Buff_Tracker_Thiet_Ke_Du_An.md](ZZZ_Drive_Disc_Buff_Tracker_Thiet_Ke_Du_An.md).

## Thu tu doc va trien khai

Xem [đánh giá khả thi và tiến độ triển khai](07-Danh-Gia-Kha-Thi-Va-Tien-Do.md) để biết quyết định mới nhất và phần đã kiểm chứng. Mã nguồn ở [main](../main/README.md).

Phase 02: [hướng dẫn overlay và checklist kiểm thử](08-Phase-02-Overlay-Va-Kiem-Thu.md).

Phase 03: [Rule Engine, quy ước nghiệp vụ và bàn giao](09-Phase-03-Rule-Engine-Va-Ban-Giao.md).

Phase 04: [profile schema, preset CRUD, import/export và hotkey](10-Phase-04-Persistence-Preset-Hotkey.md).

Phase 05: [WGC, hiệu chỉnh ROI/template, replay và kết quả kiểm chứng](11-Phase-05-Capture-Detection-Replay.md).

1. [00 - Toi uu va quyet dinh ky thuat](00-Toi-Uu-Va-Quyet-Dinh-Ky-Thuat.md)
2. [01 - Nen tang va kien truc](01-Nen-Tang-Va-Kien-Truc.md)
3. [02 - Overlay va Control Panel](02-Overlay-Va-Control-Panel.md)
4. [03 - Buff State va Rule Engine](03-Buff-State-Va-Rule-Engine.md)
5. [04 - Persistence, Preset va Hotkey](04-Persistence-Preset-Hotkey.md)
6. [05 - Capture va Detection](05-Capture-Va-Detection.md)
7. [06 - Tich hop, toi uu va phat hanh](06-Tich-Hop-Toi-Uu-Phat-Hanh.md)

## Quy tac pham vi

- Khong doc/ghi memory game, inject DLL, hook process hoac sua file cua ZZZ.
- Detection chi tao event co metadata va confidence; khong sua truc tiep BuffState.
- Domain/Application khong phu thuoc WPF, Windows API hoac OpenCV.
- Moi tinh nang auto detection phai co Manual/Hotkey fallback.
- Chi xem la hoan thanh khi co test va diagnostics phu hop, khong chi khi UI chay duoc.

## Thu tu phu thuoc

`01 -> 02 va 03 -> 04 -> 05 -> 06`

Phase 02 va 03 co the lam song song sau khi Phase 01 dat DoD. Phase 05 chi nen bat dau khi Rule Engine da chay duoc voi `GameEvent` gia lap.
