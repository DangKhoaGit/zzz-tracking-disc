# Phase 03 - Buff State va Rule Engine

Triển khai và kết quả kiểm chứng: [09 — Rule Engine và bàn giao](09-Phase-03-Rule-Engine-Va-Ban-Giao.md).

## Muc tieu

Xay dung phan nghiep vu dung va test duoc truoc khi phu thuoc vao nhan dien hinh anh.

## Pham vi

- State machine: `Inactive`, `Pending`, `Active`, `Unknown`, `Expired`.
- Xu ly `Activate`, `Refresh`, `Stack`, `Deactivate`, `Expire`, `Reset`.
- Duration tinh bang monotonic clock thong qua `IClock` injectable.
- Idempotency va deduplication theo `CorrelationKey`/cooldown.
- Session/preset version de loai event cu khi doi nhan vat.
- Policy cho mat tin hieu: timer, disappear confirmation hoac Unknown.

## Dau ra

- Cung mot event sequence cho ra cung mot ket qua.
- Buff stack khong vuot `MaxStacks`; refresh khong tao stack neu rule khong cho phep.
- Snapshot co du thong tin de Overlay chi can render.

## Kiem thu bat buoc

- Activate, refresh, expire va reset.
- Stack den gioi han va event trung.
- Event Confidence duoi nguong.
- Timestamp tre, clock bi nhay wall-clock va doi session.
- Unknown khong tu dong tat state Active neu policy khong cho phep.

## DoD

- Domain/Application test khong can WPF, OpenCV hay Windows API.
- Co benchmark nho cho event throughput va khong co race condition do state bi mutate tu nhieu worker.

## Phu thuoc

Phase 01. Phase 02 chi tieu thu snapshot, khong can cho phase nay hoan tat de lam preview.
