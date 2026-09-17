# Phase 02 - Overlay va Control Panel

## Muc tieu

Co overlay trong suot, click-through o Play Mode va Control Panel de cau hinh, preview ma khong can game that.

## Pham vi

- Tao OverlayWindow always-on-top, transparent va khong activate khi cap nhat.
- Play Mode click-through; Edit Mode cho phep keo, resize va luu vi tri.
- Hien thi Text, Image va Hybrid tu `OverlaySnapshot`.
- Countdown, progress, stack, an buff inactive va trang thai Unknown.
- Control Panel gom Dashboard, Overlay settings va preview.
- Hotkey toi thieu: toggle overlay, toggle edit mode, reset state.

## Dau ra

- Preview va overlay that dung cung mot ViewModel/state projection.
- Thay doi scale, opacity, orientation, spacing khong restart.
- Overlay khong cuop focus game va khong can thao tac chuot trong Play Mode.

## Kiem thu

- Snapshot co 0, 1 va nhieu buff.
- Timer khong am va chuyen sang Expired dung.
- Test toggle mode, luu/nap geometry.
- Manual smoke test tren man hinh 100% va 150% scaling.

## DoD

- Overlay dung duoc voi fake snapshot.
- Control Panel co the bat/tat tracking va nhin thay trang thai.
- Khong them logic detection vao WPF code-behind.

## Phu thuoc

Phase 01. Co the lam song song mot phan voi Phase 03.
