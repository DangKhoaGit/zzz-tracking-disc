# Đánh giá video ZZZ và hướng phát triển tiếp theo

Ngày: 17/09/2026. Nguồn: `main/artifacts/zzz-dataset/ZenlessZoneZero 2026-09-17 15-44-03.mp4`.

## Phạm vi quan sát

Video có thời lượng 63 giây, kích thước 1920×1080. Đã kiểm tra hình ảnh lấy mẫu mỗi 3 giây trên toàn clip; đây là khảo sát hình ảnh, không phải gán nhãn từng frame hay đánh giá độ chính xác detector. Ảnh xem trước được resize 1280×720, không dùng trực tiếp làm template chuẩn 1080p. Mốc thời gian là mốc seek gần đúng.

Ảnh tổng hợp: [contact sheet](../main/artifacts/zzz-dataset/review/contact-sheet.jpg). Các ảnh và script trích xuất nằm trong `main/artifacts/zzz-dataset/review/`.

| Mốc lấy mẫu | Quan sát | Ý nghĩa cho tracker |
| --- | --- | --- |
| 0–9 giây | HUD góc trên trái có portrait; nhân vật đang điều khiển thay đổi | Ưu tiên nhận diện portrait ở vị trí nhân vật active; không suy ra active chỉ vì portrait có mặt trong đội |
| 12–24 giây | Combat có nhiều hiệu ứng, số sát thương; HUD vẫn xuất hiện trong các mẫu | Khoanh vùng HUD, tránh nhận diện toàn cảnh hoặc lấy nút kỹ năng làm bằng chứng buff đã kích hoạt |
| 27 giây | Giao diện chọn liên kích, HUD thông thường bị thay thế | Cần phân biệt trạng thái màn hình trước khi quyết định icon biến mất |
| 30–42 giây | Menu sân huấn luyện, thông tin kỹ năng và thuộc tính | Không chạy detector combat trên menu; trang thuộc tính có thể hỗ trợ đối chiếu thủ công |
| 45 và 48 giây | “Chi Tiết Hiệu Quả”, lần lượt chọn Norma và Astra Yao; cùng thấy “Bị Động Cốt Lõi: Nhịp Điệu Thong Thả”, “Còn 28,0s”, tăng tấn công 1095 điểm; “Mưa Đạn Um Ne” có mô tả hiệu ứng toàn đội | Có nguồn đối chiếu tên/hiệu ứng/thời gian còn lại. Hai mẫu cùng 28,0s gợi ý timer được dừng trong menu; cần kiểm chứng riêng trước khi áp dụng quy tắc chung |
| 51–60 giây | Quay lại combat, tiếp tục đổi nhân vật và dùng kỹ năng | Có thể làm đoạn kiểm tra phục hồi sau menu và chuyển nhân vật |

Tên và số trên được đọc từ video, không phải danh mục cơ chế game đã được kiểm chứng. Clip chưa cung cấp đủ bằng chứng để ánh xạ các hiệu ứng này sang buff bộ đĩa cụ thể, duration đầy đủ, điều kiện kích hoạt/refresh hay stack. Không lấy 28 giây còn lại làm duration mặc định.

## Khoảng trống trong mã hiện tại

1. `TemplateDetector.cs` so khớp Gray8 trên ROI cố định. Score thấp đủ số frame có thể phát `BuffIconDisappeared`, kể cả khi HUD bị thay bởi menu/liên kích. Capture vẫn hoạt động nên kiểm tra mất capture không giải quyết trường hợp này.
2. `TrackingService.cs` tạo context/session và Rule Engine mới khi nhận `CharacterChanged`. Cần mở rộng mô hình trước khi hỗ trợ buff tồn tại ngoài sân hoặc toàn đội; đổi nhân vật không nên tự động làm mất mọi state chiến đấu.
3. Chưa có trạng thái gameplay pause được xác nhận từ hình ảnh. Pause detection hiện vẫn giữ timer chạy; không đồng nghĩa dừng thời gian trong game.
4. Replay hiện nhận manifest và Gray8, chưa nhập MP4. Giới hạn 60 giây, 300 frame và 128 MB. Một frame Gray8 1080p khoảng 2 MB: ở 10 Hz, giới hạn dung lượng chỉ đủ khoảng 6 giây. Không chuyển nguyên clip thành một replay duy nhất; nên chia đoạn khoảng 3–5 giây với metadata và timestamp nguồn.
5. Evaluator đang so event đúng frame, không chạy Rule Engine. Cần thêm đánh giá thời điểm event với dung sai và kịch bản state end-to-end; không gán ground truth theo chính frame debounce của detector.

## Thứ tự triển khai đề xuất

### 1. Hoàn thiện Phase 05 bằng dữ liệu thật

- Tạo công cụ MP4 → các đoạn replay ngắn; giữ video gốc, resolution, mốc bắt đầu, FPS lấy mẫu và cấu hình UI nếu biết. Giữ nhãn chưa biết là null.
- Gán nhãn combat/menu/liên kích, nhân vật active, HUD nhìn thấy/không nhìn thấy trước. Chọn các mẫu 45/48 giây làm bằng chứng đối chiếu buff, chưa dùng làm template combat.
- Tách đoạn hiệu chỉnh và kiểm tra; bổ sung clip độc lập sau vì các frame cạnh nhau trong cùng video không chứng minh khả năng tổng quát.

### 2. Nhận diện trạng thái màn hình và nhân vật

- Thêm trạng thái Combat, Menu, ChainSelection, Unknown. Chỉ phát quyết định absent khi vùng HUD cần thiết thực sự quan sát được; HUD không quan sát được phải đi theo chính sách mất tín hiệu.
- Bắt đầu với portrait active góc trên trái và ba nhân vật có trong clip. Menu cũng có portrait, nên phải kiểm tra trạng thái màn hình trước.
- So sánh matcher hiện tại trên positive/negative thật; chỉ nâng sang màu, mask, NCC hoặc tìm kiếm cục bộ nếu kết quả cho thấy cần. Chưa đủ dữ liệu để chọn ML làm bước đầu.

### 3. Giữ buff qua đổi nhân vật và xử lý thời gian game

- Tách phiên chiến đấu khỏi nhân vật active; lưu owner, đối tượng hưởng và scope cá nhân/toàn đội. Quy tắc cụ thể quyết định buff có tiếp tục ngoài sân hay không.
- Thêm chính sách thời gian khi menu thực sự pause game; tách gameplay clock khỏi đồng hồ đo latency/capture. Không mặc định mọi cutscene, liên kích hoặc mất HUD đều dừng timer.
- Kiểm thử đổi A → B → A, buff toàn đội, vào/ra menu, mất tín hiệu và manual fallback; tránh reset hoặc refresh giả khi HUD xuất hiện lại.

### 4. Một buff thật xuyên suốt pipeline

- Chọn một buff có điều kiện kích hoạt xác nhận được, gán bằng chứng HUD và rule; nếu mục tiêu là buff bộ đĩa, cần xác nhận bộ đĩa đang trang bị và cơ chế tương ứng.
- Dùng trang “Chi Tiết Hiệu Quả” đối chiếu thủ công trước. OCR là bước bổ sung để hỗ trợ đọc menu/hiệu chỉnh; không giả định menu là nguồn realtime luôn có sẵn trong combat.
- Phân biệt timer được quan sát/xác nhận với timer suy luận từ sự kiện và duration. Khi chưa chắc, hiển thị Unknown thay vì thời gian chính xác giả.

## Điều kiện chuyển sang Phase 06

- Có replay gán nhãn thật và report TP/FP/FN, độ trễ event; công bố rõ phạm vi một clip/1080p và nhãn chưa biết.
- Đã kiểm thử không phát hết buff giả khi menu/liên kích che HUD, không mất buff trái quy tắc khi đổi nhân vật, không chạy timer sai qua menu pause.
- Có ít nhất một buff đã xác minh từ trigger → state → overlay → hết hạn/refresh; thêm video độc lập để kiểm tra.
- Sau đó mới mở rộng resolution/UI scale, số buff, profiling capture dài và đóng gói. Chưa có căn cứ kết luận Phase 05 đã đạt độ chính xác trong game chỉ từ video này.

Đánh giá này không thay đổi mã ứng dụng và không chạy lại bộ test; các nhận xét về hành vi hiện tại dựa trên đọc mã và tài liệu Phase 05.
