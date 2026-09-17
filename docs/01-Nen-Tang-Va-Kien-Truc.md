# Phase 01 - Nen tang va kien truc

## Muc tieu

Tao solution .NET/WPF co ranh gioi module ro rang va chay duoc mot vertical slice toi thieu bang du lieu gia lap.

## Pham vi

- Tao solution va cac project App, Application, Domain, Detection, Infrastructure, Tests.
- Cau hinh MVVM, dependency injection, logging va cancellation lifecycle.
- Dinh nghia interface: `IGameEventSource`, `IBuffStateStore`, `IPresetRepository`, `IClock`.
- Dinh nghia model co ban: `GameEvent`, `BuffDefinition`, `BuffState`, `CharacterPreset`, `OverlaySnapshot`.
- Tao fake event source de phat event trong development.

## Dau ra

- Ung dung mo duoc Control Panel toi thieu.
- Start/Stop tracking khong tao worker trung va shutdown sach.
- Fake `ManualTrigger` di qua Application toi snapshot state.
- Logging co correlation id va muc do loi.

## Kiem thu

- Build solution tren .NET 8+.
- Unit test event pipeline va lifecycle start/stop hai lan.
- Test khong co UI dependency trong Domain/Application.
- Test cancellation khong treo khi dong ung dung.

## DoD

- `dotnet build` va test pass.
- Co README cua solution neu lenh chay khac mac dinh.
- Chua ket noi OpenCV hay capture that o phase nay.

## Phu thuoc

Khong co. Day la phase nen.
