# 📋 Chính sách & Miễn trừ trách nhiệm

## Tuyên bố miễn trừ trách nhiệm (Disclaimer)

### Về nguồn dữ liệu

**HCMC Rain Vision** sử dụng dữ liệu hình ảnh từ các nguồn công khai:

- **Nguồn chính:** Cổng thông tin giao thông TP.HCM (http://giaothong.hochiminhcity.gov.vn)
- **Mục đích:** Phục vụ nghiên cứu, học tập và cung cấp thông tin thời tiết cho cộng đồng
- **Tính chất:** Dự án phi lợi nhuận, mã nguồn mở

### Tuyên bố quan trọng

1. **Chúng tôi KHÔNG:**
   - Sở hữu hình ảnh camera từ hệ thống giao thông TP.HCM
   - Lưu trữ toàn bộ video/ảnh từ camera (chỉ lưu snapshot phát hiện mưa)
   - Sử dụng dữ liệu cho mục đích thương mại
   - Can thiệp vào hệ thống camera gốc

2. **Chúng tôi CAM KẾT:**
   - Luôn ghi rõ nguồn dữ liệu
   - Tôn trọng băng thông và tài nguyên của server nguồn
   - Tuân thủ tần suất truy cập hợp lý (mặc định 5 phút/lần)
   - Xóa dữ liệu cũ sau 7 ngày để tiết kiệm lưu trữ
   - Ngừng hoạt động ngay nếu được yêu cầu bởi chủ sở hữu dữ liệu

### Độ chính xác thông tin

- ⚠️ Thông tin dự báo mưa được tạo bởi AI với độ chính xác **không phải 100%**
- ⚠️ Người dùng nên tham khảo thêm nguồn thông tin chính thức từ:
  - Đài Khí tượng Thủy văn Khu vực Nam Bộ
  - Trung tâm Dự báo Khí tượng Thủy văn Quốc gia
- ⚠️ Hệ thống chỉ phục vụ **tham khảo**, không thay thế cảnh báo chính thức

## 📜 Bản quyền & Thuộc quyền

### Dữ liệu camera
- **Bản quyền:** Thuộc về Sở Giao thông Vận tải TP.HCM
- **Truy cập:** Qua API công khai tại http://giaothong.hochiminhcity.gov.vn
- **Sử dụng:** Tuân thủ chính sách của Cổng thông tin giao thông

### Mã nguồn dự án
- **Giấy phép:** MIT License (Mã nguồn mở)
- **Repository:** https://github.com/KhaiMinhVo/HcmcRainVision.Backend
- **Quyền:** Cho phép sử dụng, sửa đổi, phân phối với điều kiện giữ nguyên thông tin tác giả

### AI Model & Thuật toán
- **Mô hình AI:** Tự phát triển hoặc sử dụng pre-trained models (ML.NET)
- **Training data:** Tổng hợp từ nhiều nguồn công khai
- **Độ chính xác:** Được cải thiện liên tục qua user feedback

## 🔒 Chính sách bảo mật

### Thu thập dữ liệu người dùng
Chúng tôi thu thập tối thiểu:
- Email (cho chức năng đăng ký/đăng nhập)
- Vị trí GPS (nếu user cho phép, để tính khoảng cách đến vùng mưa)
- User reports (phản hồi về độ chính xác AI)

### Không chia sẻ dữ liệu
- ✅ Không bán dữ liệu cho bên thứ ba
- ✅ Không chia sẻ thông tin cá nhân
- ✅ Chỉ sử dụng để cải thiện hệ thống

### Cookies & Tracking
- Sử dụng JWT token để xác thực (không dùng cookies phức tạp)
- Không có tracking/analytics xâm nhập
- Log hệ thống chỉ chứa IP, timestamp và camera ID (không lưu thông tin cá nhân)

## ⚖️ Tuân thủ pháp luật

### Luật bảo vệ dữ liệu cá nhân (Việt Nam)
- Tuân thủ Nghị định 13/2023/NĐ-CP về bảo vệ dữ liệu cá nhân
- Người dùng có quyền xóa tài khoản và dữ liệu bất cứ lúc nào
- Mật khẩu được mã hóa BCrypt (không lưu plaintext)

### Luật An toàn thông tin mạng
- Không thu thập dữ liệu trái phép
- Không tấn công/làm gián đoạn hệ thống khác
- Có cơ chế rate limiting để tránh quá tải

## 🤝 Chính sách truy cập API

### Tần suất truy cập camera
- **Mặc định:** 5 phút/lần quét toàn bộ cameras
- **Tối thiểu:** Không quét nhanh hơn 2 phút/lần
- **Giới hạn:** Tối đa 5 cameras song song (tránh overload)

### User-Agent & Identification
```http
User-Agent: HcmcRainVision/1.0 (+https://github.com/KhaiMinhVo/HcmcRainVision.Backend)
Referer: http://giaothong.hochiminhcity.gov.vn/
```

### Retry Policy
- Tối đa **3 lần thử** nếu request bị lỗi
- Delay **1 giây** giữa mỗi lần thử
- Timeout **10 giây** cho mỗi request

### Respect for robots.txt
- Kiểm tra `robots.txt` của server nguồn
- Tôn trọng `Crawl-delay` nếu có
- Ngừng ngay nếu bị `403 Forbidden` liên tục

## 📞 Liên hệ & Yêu cầu gỡ bỏ

### Nếu bạn là quản trị viên hệ thống camera gốc:
Nếu bạn muốn chúng tôi ngừng sử dụng dữ liệu, vui lòng liên hệ:

- **Email:** khaivpmse184623@fpt.edu.vn
- **GitHub Issues:** https://github.com/KhaiMinhVo/HcmcRainVision.Backend/issues
- **Response time:** Trong vòng 24 giờ

Chúng tôi cam kết:
- ✅ Ngừng thu thập dữ liệu ngay lập tức
- ✅ Xóa dữ liệu đã lưu trữ (nếu yêu cầu)
- ✅ Cung cấp báo cáo về việc sử dụng dữ liệu

### Nếu bạn là người dùng:
- **Báo lỗi:** Qua GitHub Issues
- **Feature request:** Pull Requests luôn được chào đón
- **Phản hồi AI:** Sử dụng chức năng "Report" trong app

## 🎯 Mục đích dự án

### Mục tiêu chính
1. **Nghiên cứu khoa học:** Phát triển AI phát hiện mưa từ hình ảnh
2. **Phục vụ cộng đồng:** Cảnh báo mưa sớm cho người đi đường
3. **Học tập:** Chia sẻ kiến thức về Computer Vision, .NET, PostgreSQL GIS

### Không phải là
- ❌ Dịch vụ thương mại
- ❌ Sản phẩm hoàn thiện (vẫn đang phát triển)
- ❌ Thay thế nguồn tin chính thức

## 🔄 Quyền sửa đổi

Chúng tôi có quyền cập nhật chính sách này bất cứ lúc nào. Phiên bản mới sẽ được công bố tại:
- Repository GitHub (file này)
- Website chính thức (nếu có)
- Email thông báo cho registered users (nếu có thay đổi quan trọng)

## ✅ Cam kết hiện tại

- [x] User-Agent rõ ràng với link dự án
- [x] Referer header đúng theo yêu cầu
- [x] Tần suất truy cập hợp lý (5 phút)
- [x] Retry mechanism có delay
- [x] Timeout để tránh treo connection
- [x] Tự động dọn dẹp data cũ (7 ngày)
- [x] Health check để phát hiện URL hỏng
- [x] Ghi rõ nguồn trên Frontend
- [x] Mã nguồn công khai trên GitHub

---

**Cập nhật lần cuối:** 2 tháng 2, 2026  
**Phiên bản:** 1.0  
**Dự án:** HCMC Rain Vision  
**Tác giả:** Khai Minh Vo
