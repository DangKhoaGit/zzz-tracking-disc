# Toi uu va quyet dinh ky thuat

Tai lieu nay chuyen cac y tuong trong ban thiet ke thanh cac quyet dinh co the kiem chung trong qua trinh lam MVP.

## 1. Uu tien do tin cay tracking

### 1.1. Khong suy ra buff chi tu mot frame

Moi detector phat hien theo chu ky phai co:

- `candidate`: ket qua cua frame hien tai.
- `consecutiveHits`: so frame lien tiep phat hien.
- `consecutiveMisses`: so frame lien tiep khong phat hien.
- `confidence`: diem tin cay cua ket qua.
- `lastConfirmedAt`: thoi diem ket qua duoc xac nhan.

Chi phat `Appeared` sau N frame dat nguong. Chi phat `Disappeared` sau M frame mat tin hieu. N va M phai cau hinh duoc theo detector de tranh nhap nhay.

### 1.2. Event phai co identity va deduplication

Moi `GameEvent` nen co `EventId`, `Type`, `SubjectId`, `CapturedAt`, `Confidence`, `Source` va `CorrelationKey`. Event trung trong cooldown khong duoc lam tang stack hoac refresh lai buff.

`CapturedAt` dung de theo doi do tre; `ProcessedAt` dung cho diagnostics. Khong dung thoi gian UI lam nguon tinh duration.

### 1.3. Confidence theo event, khong dung mot threshold chung

Character detection, buff icon detection va combat detection co muc do kho khac nhau. Moi detector nen co threshold rieng, cung voi trang thai `Unknown` khi confidence nam trong vung khong chac chan. `Unknown` khong duoc tu dong tat buff vua dang Active.

## 2. Mo hinh tracking de tranh sai timer

- Dung `Stopwatch.GetTimestamp()` hoac monotonic clock cho duration, khong dung `DateTime.Now` de tinh countdown.
- State nen co `Inactive`, `Pending`, `Active`, `Unknown`, `Expired` thay vi chi co active/inactive.
- Timer UI chi la projection tu `ExpiresAt - now`; khong tao mot timer rieng cho moi buff.
- Buff khong co detection mat tin hieu phai co policy rieng: `ExpireByTimer`, `WaitForDisappear`, hoac `KeepUnknown`.
- Khi doi nhan vat, dung `SessionId`/`PresetVersion` de bo qua event cu tu preset truoc.

## 3. Capture va detection

- Chi capture cua so/monitor da chon va crop ROI truoc khi xu ly.
- Giu capture loop o muc 10-20 FPS; UI render doc lap.
- Giam FPS khi cua so game mat focus hoac dang o menu khong can tracking.
- Cache template da load; khong doc file anh trong moi frame.
- Dung queue co gioi han va bo frame cu khi detector cham, thay vi de backlog lam tang latency.
- Luu screenshot/replay co che do an danh de tai hien false positive va false negative.

## 4. Kien truc va concurrency

- Mot worker duy nhat duoc phep mutate BuffState.
- Channel event co bounded capacity va backpressure; khong drop semantic event nhu Appeared/Disappeared/CharacterChanged/ManualTrigger. Chi dung `DropOldest` cho raw frame truoc detector; manual command bi tu choi phai bao ro cho UI.
- UI nhan snapshot bat bien thay vi doc truc tiep collection dang bi mutate.
- Start/stop tracking phai idempotent; huy capture, worker va hotkey theo cung mot lifecycle.

## 5. UX va fallback

- Hien thi ro `Tracking`, `Paused`, `Manual`, `Unknown` tren Control Panel.
- Overlay khong nen hien thi countdown gia tao khi detection khong chac chan; dung icon/nhan `?` hoac giu gia tri cu tuy policy.
- Co nut `Reset state`, `Manual trigger`, `Pause detection` va `Test current ROI`.
- Edit Mode phai co quy trinh an toan: bat edit, keo/resize, luu, quay lai Play Mode.
- Khi mat cua so ZZZ, giu cau hinh va chuyen `GameNotFound`, khong crash hoac xoa preset.

## 6. Diagnostics va KPI

Theo doi toi thieu:

- Capture FPS, detection FPS, queue depth, event latency.
- CPU/RAM va thoi gian xu ly tung detector.
- Confidence phan bo theo detector.
- So false positive/false negative tu replay dataset.
- Ty le auto character switch thanh cong.

Muc tieu MVP nen la latency event-to-overlay < 150 ms trong dieu kien binh thuong, khong phai toi uu FPS bang moi gia. Threshold do tin cay phai duoc chot bang screenshot dataset co version.

## 7. Thu tu uu tien

1. State machine va Rule Engine dung.
2. Manual/Hotkey fallback va diagnostics.
3. Overlay khong gay can tro input.
4. Capture ROI on dinh.
5. Template matching va auto detection.
6. OCR, auto doc build va cac detector nang cao.

OCR va nhan dien build truc tiep khong nen nam trong MVP vi rui ro cao hon gia tri ban dau.
