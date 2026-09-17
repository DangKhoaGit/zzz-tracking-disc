# ZZZ Character Buff Tracker

Companion desktop Windows cho Zenless Zone Zero, dùng C# / .NET 10 / WPF.

Theo dõi **buff của nhân vật qua icon trên HUD**. Tạo buff trong preset nhân vật, rồi tạo template **Buff nhân vật** với `Subject ID` trùng `Buff ID` và `Character ID` trùng preset. Icon xuất hiện/biến mất sẽ cập nhật trạng thái buff; thời lượng là giá trị cấu hình, chưa đọc trực tiếp từ game. Không cần chọn set đĩa. Profile và template cũ vẫn được hỗ trợ.

Đã triển khai **Phase 01–04 và pipeline Phase 05**: overlay, Rule Engine, preset/profile, manual hotkey, Windows Graphics Capture, ROI/template và replay. Capture đã kiểm thử trên cửa sổ thử; chưa có template/dataset ZZZ được xác minh nên chưa chốt độ chính xác nhận diện trong game.

- [Chạy ứng dụng và kiểm thử](main/README.md)
- [Đánh giá khả thi, quyết định kỹ thuật và tiến độ](docs/07-Danh-Gia-Kha-Thi-Va-Tien-Do.md)
- [Tài liệu thiết kế và lộ trình](docs/README.md)
- [Hướng dẫn overlay và checklist Phase 02](docs/08-Phase-02-Overlay-Va-Kiem-Thu.md)
- [Rule Engine và bàn giao Phase 03](docs/09-Phase-03-Rule-Engine-Va-Ban-Giao.md)
- [Presets/Profiles và hotkey Phase 04](docs/10-Phase-04-Persistence-Preset-Hotkey.md)
- [Capture, Detection, Replay và hiệu chỉnh Phase 05](docs/11-Phase-05-Capture-Detection-Replay.md)

Chạy từ thư mục gốc `zzz-tracking-disc`:

```powershell
dotnet run --project .\main\ZZZBuffTracker.App\ZZZBuffTracker.App.csproj
```

Nếu terminal đã ở trong `main`, dùng `dotnet run --project .\ZZZBuffTracker.App\ZZZBuffTracker.App.csproj`.

Dự án vẫn đang phát triển, chưa là sản phẩm chính thức. Mọi người có thể phát triển thêm để có một công cụ hữu ích trong quá trình chơi game
