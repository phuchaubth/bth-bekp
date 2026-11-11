# Tài liệu yêu cầu nghiệp vụ (SRS) — Quản lý Phiếu đồng thuận

> Phiên bản: 1.0 (Sprint 0) · Trạng thái: **Đã phê duyệt kế hoạch**, chi tiết sẽ cập nhật khi có mẫu phiếu, tài liệu HIS và thông tin SmartCA UAT.
> Tài liệu liên quan: [Kế hoạch triển khai](KE_HOACH_TRIEN_KHAI.md) · [Kiến trúc](kien-truc.md) · [Tích hợp SmartCA](tich-hop-smartca.md)
>
> Mọi ví dụ trong tài liệu là dữ liệu giả lập. Không dùng dữ liệu người bệnh thật.

---

## 1. Giới thiệu

### 1.1. Mục đích

Số hóa việc lập, ký và lưu trữ các phiếu đồng thuận/cam kết của người bệnh trước khi khám bệnh, chữa bệnh. Hệ thống ký số bằng VNPT SmartCA, lấy thông tin từ QR CCCD và HIS FPT eHospital, và cho phép tra cứu, xuất dữ liệu có kiểm soát.

### 1.2. Phạm vi giai đoạn 1

> **Cập nhật 11/11/2026:** tạm **chưa kết nối HIS và SmartCA**. Các yêu cầu FR-HIS-01…03 và FR-SIGN (SmartCA) giữ nguyên làm thiết kế, sẽ thực hiện khi bệnh viện cần. Ba mẫu phiếu dùng [mẫu đề xuất](mau-phieu-de-xuat.md) trong khi chờ duyệt.

| Trong phạm vi | Ngoài phạm vi (giai đoạn sau) |
|---|---|
| 3 loại phiếu: **Cam kết sử dụng dịch vụ khám (DV/BHYT)**, **Phiếu cung cấp thông tin**, **Phiếu khảo sát nguy cơ té ngã** | Phiếu đồng thuận chuyên môn: phẫu thuật, thủ thuật, gây mê, truyền máu… |
| Quét QR CCCD, tra cứu HIS FPT eHospital (chỉ đọc) | Ghi ngược phiếu/PDF vào HIS/EMR |
| Ký số VNPT SmartCA cho người bệnh, người ký thay, người chứng kiến và nhân viên bệnh viện; ký tay là phương án dự phòng | Ký không cần xác nhận trên app (SmartCA TH), chữ ký sinh trắc học |
| Tài khoản nội bộ, phân quyền theo vai trò và khoa | Đăng nhập một lần (SSO/AD) |
| Xuất Excel/CSV, báo cáo, nhật ký audit | Ứng dụng di động riêng |

### 1.3. Thuật ngữ

| Thuật ngữ | Ý nghĩa |
|---|---|
| Phiếu | Một phiếu đồng thuận/cam kết cụ thể đã lập cho một người bệnh |
| Mẫu phiếu | Định nghĩa nội dung, các trường cần điền và cấu hình ký của một loại phiếu, có phiên bản |
| Người ký phía người bệnh | Người bệnh, hoặc người ký thay (PA3), hoặc người bệnh ký tay kèm người chứng kiến (PA2) |
| Người ký phía bệnh viện | Nhân viên ký xác nhận phiếu; vai trò được **cấu hình theo từng mẫu** (bác sĩ / điều dưỡng / tiếp đón) |
| PA1 / PA2 / PA3 | Phương án ký phía người bệnh: SmartCA của người bệnh / ký tay + chứng kiến / SmartCA của người ký thay |
| HIS | Hệ thống thông tin bệnh viện FPT eHospital |

---

## 2. Tác nhân và vai trò

| Vai trò | Quyền chính |
|---|---|
| Quản trị hệ thống | Người dùng, vai trò, khoa/phòng, cấu hình, danh mục lý do, xem nhật ký |
| Quản lý mẫu phiếu | Tạo/sửa/phát hành/ngừng mẫu phiếu theo phiên bản |
| Bác sĩ | Lập phiếu, ký phía bệnh viện (nếu mẫu cấu hình), xem phiếu trong khoa |
| Điều dưỡng | Lập phiếu, quét CCCD, ký phía bệnh viện (nếu mẫu cấu hình), làm người chứng kiến |
| Tiếp đón | Quét CCCD, lập phiếu cam kết DV/BHYT, ký phía bệnh viện (nếu mẫu cấu hình), làm người chứng kiến |
| Trưởng khoa | Xem, thống kê, xuất dữ liệu trong phạm vi khoa |
| Báo cáo / Xuất dữ liệu | Tra cứu, xuất dữ liệu toàn viện |
| Kiểm tra | Chỉ đọc phiếu và nhật ký audit |

Quyền được cấp theo **permission** gán vào vai trò, kèm **phạm vi dữ liệu theo khoa**. Bất kỳ nhân viên nào có tài khoản và SmartCA đều làm người chứng kiến được (BR-08).

---

## 3. Yêu cầu chức năng

Mức ưu tiên: **M** = bắt buộc giai đoạn 1, **S** = nên có. Cột Sprint theo mục 8 của [kế hoạch](KE_HOACH_TRIEN_KHAI.md).

### 3.1. Xác thực và phân quyền (AUTH)

| Mã | Yêu cầu | Ưu tiên | Sprint |
|---|---|---|---|
| FR-AUTH-01 | Nhân viên đăng nhập bằng tên đăng nhập và mật khẩu | M | 1 |
| FR-AUTH-02 | Khóa tạm thời tài khoản sau 5 lần đăng nhập sai liên tiếp | M | 1 |
| FR-AUTH-03 | Phiên đăng nhập dùng access token ngắn hạn và refresh token xoay vòng (cookie HttpOnly); refresh token đã dùng thì không dùng lại được | M | 1 |
| FR-AUTH-04 | Người dùng đổi mật khẩu; quản trị khóa/mở khóa tài khoản | M | 1 |
| FR-AUTH-05 | Mỗi API kiểm tra permission; người dùng chỉ thấy dữ liệu trong khoa của mình, trừ vai trò toàn viện | M | 1 |
| FR-AUTH-06 | Giao diện ẩn/hiện menu và nút theo quyền | M | 1 |

### 3.2. Người bệnh và QR CCCD (PAT)

| Mã | Yêu cầu | Ưu tiên | Sprint |
|---|---|---|---|
| FR-PAT-01 | Quét QR CCCD bằng camera (máy tính bảng/điện thoại/webcam) | M | 2 |
| FR-PAT-02 | Nhận chuỗi QR từ máy quét mã vạch 2D dạng bàn phím | M | 2 |
| FR-PAT-03 | Phân tích chuỗi QR `Số CCCD\|Số CMND cũ\|Họ tên\|Ngày sinh\|Giới tính\|Địa chỉ\|Ngày cấp` và điền form; báo lỗi rõ khi chuỗi sai định dạng | M | 2 |
| FR-PAT-04 | Tìm hồ sơ người bệnh đã có theo số CCCD; nếu đã có thì đề nghị cập nhật, không tạo bản trùng | M | 2 |
| FR-PAT-05 | Bổ sung thông tin còn thiếu: SĐT, nghề nghiệp, dân tộc, địa chỉ hiện tại, mã BHYT…; địa chỉ luôn sửa được | M | 2 |
| FR-PAT-06 | Khai báo người đại diện/người ký thay, quét QR CCCD của người đó | M | 2 |
| FR-PAT-07 | Che số CCCD, SĐT, mã BHYT trên giao diện danh sách (ví dụ `0790******12`) | M | 2 |

### 3.3. Tích hợp HIS FPT eHospital (HIS)

| Mã | Yêu cầu | Ưu tiên | Sprint |
|---|---|---|---|
| FR-HIS-01 | Sau khi quét CCCD, tra cứu HIS để lấy mã người bệnh, đợt khám/tiếp nhận, đối tượng BHYT/dịch vụ, số thẻ BHYT, khoa/phòng khám | M | 2 (giả lập), khi có tài liệu API (thật) |
| FR-HIS-02 | HIS lỗi/quá thời gian: vẫn nhập tay được, phiếu được đánh dấu "chưa đối chiếu HIS" | M | 2 |
| FR-HIS-03 | Giai đoạn 1 chỉ đọc từ HIS, không ghi ngược | M | — |

### 3.4. Mẫu phiếu (TPL)

| Mã | Yêu cầu | Ưu tiên | Sprint |
|---|---|---|---|
| FR-TPL-01 | Quản lý mẫu phiếu có phiên bản: Nháp → Phát hành → Ngừng sử dụng | M | 3 |
| FR-TPL-02 | Định nghĩa trường động: văn bản, ngày, lựa chọn đơn/nhiều, checkbox, câu hỏi có điểm (cho phiếu khảo sát té ngã) | M | 3 |
| FR-TPL-03 | Cấu hình theo mẫu: **vai trò ký phía bệnh viện**, bật/tắt PA2 và PA3, khoa áp dụng | M | 3–4 |
| FR-TPL-04 | Dựng sẵn 3 mẫu giai đoạn 1 theo mẫu giấy bệnh viện cung cấp | M | 3 |
| FR-TPL-05 | Phiếu luôn giữ nguyên phiên bản mẫu tại thời điểm lập | M | 3 |

### 3.5. Lập và quản lý phiếu (FORM)

| Mã | Yêu cầu | Ưu tiên | Sprint |
|---|---|---|---|
| FR-FORM-01 | Lập phiếu theo wizard: người bệnh → chọn mẫu và điền nội dung → xem trước và ký | M | 3 |
| FR-FORM-02 | Sinh PDF từ mẫu và dữ liệu, hiển thị đúng tiếng Việt có dấu | M | 3 |
| FR-FORM-03 | Danh sách phiếu có lọc (trạng thái, khoa, loại phiếu, ngày, từ khóa), phân trang ở máy chủ | M | 3 |
| FR-FORM-04 | Chi tiết phiếu: thông tin, PDF, danh sách chữ ký, lịch sử trạng thái; in/tải PDF | M | 3 |
| FR-FORM-05 | Thu hồi phiếu đã hoàn tất, bắt buộc nhập lý do; lập phiếu mới tham chiếu phiếu cũ | M | 4 |
| FR-FORM-06 | Ghi nhận quyết định của người bệnh theo nội dung mẫu (ví dụ chọn khám DV hay BHYT; đồng ý/từ chối) | M | 3 |

### 3.6. Ký số (SIGN)

| Mã | Yêu cầu | Ưu tiên | Sprint |
|---|---|---|---|
| FR-SIGN-01 | Phía người bệnh ký theo thứ tự ưu tiên **PA1 → PA3 → PA2**; giao diện hiển thị và gợi ý theo đúng thứ tự | M | 4 |
| FR-SIGN-02 | **PA1:** người bệnh ký bằng SmartCA cá nhân (định danh bằng số CCCD), xác nhận trên app SmartCA | M | 4 |
| FR-SIGN-03 | **PA3:** người ký thay ký bằng SmartCA của họ; bắt buộc quét CCCD người ký thay, chọn quan hệ và lý do | M | 4 |
| FR-SIGN-04 | **PA2:** người bệnh ký tay trên màn hình cảm ứng, bắt buộc lý do; sau đó **người chứng kiến ký SmartCA** xác nhận | M | 4 |
| FR-SIGN-05 | Người ký phía bệnh viện (theo cấu hình mẫu) ký SmartCA sau cùng; phiếu hoàn tất có **2 chữ ký số hợp lệ** | M | 4 |
| FR-SIGN-06 | Trước khi ký, tra cứu chứng thư số; báo rõ khi không có tài khoản, chứng thư hết hạn/không hợp lệ, **hết lượt ký** (lượt ký trừ vào gói của người bệnh) và gợi ý phương án tiếp theo | M | 4 |
| FR-SIGN-07 | Màn hình ký hiển thị mã giao dịch trùng với mã trên app SmartCA và đếm ngược thời gian chờ (300 giây) | M | 4 |
| FR-SIGN-08 | Xử lý được: thành công, người ký từ chối, hết hạn, lỗi mạng/hệ thống (thử lại có giới hạn) | M | 4 |
| FR-SIGN-09 | Lý do ký tay/ký thay chọn từ **danh mục cấu hình được** (bệnh viện bổ sung danh sách chính thức sau) | M | 4 |

### 3.7. Xuất dữ liệu, báo cáo, nhật ký (EXP, RPT, AUD)

| Mã | Yêu cầu | Ưu tiên | Sprint |
|---|---|---|---|
| FR-EXP-01 | Xuất Excel/CSV theo bộ lọc, chọn cột, tiêu đề tiếng Việt | M | 5 |
| FR-EXP-02 | Số CCCD mặc định bị che; chỉ quyền `consent.export.sensitive` mới xuất được bản đầy đủ | M | 5 |
| FR-EXP-03 | Ghi lịch sử mọi lần xuất (ai, khi nào, bộ lọc, số dòng) | M | 5 |
| FR-RPT-01 | Dashboard: số phiếu theo trạng thái, phiếu chờ ký của tôi | M | 5 |
| FR-RPT-02 | Thống kê theo khoa, loại phiếu, tỉ lệ từng phương án ký, thời gian hoàn tất | S | 5 |
| FR-AUD-01 | Ghi audit cho đăng nhập, mọi thao tác ghi, ký, thu hồi, xuất dữ liệu, chọn PA2/PA3 | M | 1 trở đi |
| FR-AUD-02 | Màn hình tra cứu audit theo người dùng, hành động, đối tượng, thời gian | M | 5 |

### 3.8. Quản trị (ADM)

| Mã | Yêu cầu | Ưu tiên | Sprint |
|---|---|---|---|
| FR-ADM-01 | Quản lý người dùng: tạo/sửa/khóa, gán vai trò, khoa, định danh SmartCA | M | 1 |
| FR-ADM-02 | Ma trận quyền theo vai trò | M | 1 |
| FR-ADM-03 | Danh mục khoa/phòng | M | 1 |
| FR-ADM-04 | Cấu hình: môi trường SmartCA (không hiển thị secret), thời hạn giao dịch, bật/tắt PA2/PA3, danh mục lý do | M | 4 |

---

## 4. Quy tắc nghiệp vụ

| Mã | Quy tắc |
|---|---|
| BR-01 | Phiếu đã hoàn tất là **bất biến**. Muốn sửa phải thu hồi (ghi lý do) và lập phiếu mới tham chiếu phiếu cũ |
| BR-02 | Phía người bệnh luôn ký trước, người ký phía bệnh viện ký sau cùng |
| BR-03 | Thứ tự ưu tiên phương án ký phía người bệnh: PA1 → PA3 → PA2 |
| BR-04 | Chứng thư SmartCA phải đang hoạt động, còn hạn, loại cá nhân (không phải ESEAL), và có số CCCD trong chủ thể **khớp** với người ký đã khai báo |
| BR-05 | PA2 và PA3 bắt buộc có lý do (từ danh mục); PA3 bắt buộc có quan hệ với người bệnh |
| BR-06 | Người ký thay (PA3) không được là người ký phía bệnh viện hoặc nhân viên lập phiếu |
| BR-07 | PA2: người bệnh ký tay xong thì người chứng kiến phải ký SmartCA trước khi người ký phía bệnh viện ký |
| BR-08 | Người chứng kiến là **bất kỳ nhân viên nào, miễn là người thứ 3**: không phải người ký phía bệnh viện của phiếu và không phải người ký phía người bệnh |
| BR-09 | PA2/PA3 có thể bị tắt theo khoa hoặc loại phiếu; khi tắt thì giao diện ẩn và API từ chối |
| BR-10 | Vai trò ký phía bệnh viện do mẫu phiếu quy định; người ký phải có vai trò đó và có SmartCA |
| BR-11 | Hệ thống **không tự động xóa** phiếu; thời hạn lưu trữ theo quy định hồ sơ bệnh án |
| BR-12 | Không lưu chuỗi QR thô; chỉ lưu các trường đã chuẩn hóa |

---

## 5. Yêu cầu phi chức năng

| Mã | Nhóm | Yêu cầu |
|---|---|---|
| NFR-01 | Bảo mật | HTTPS; mật khẩu băm (ASP.NET Core Identity); rate limiting đăng nhập; CORS chỉ cho origin được cấu hình; security headers; không trả chi tiết lỗi nội bộ cho client |
| NFR-02 | Bí mật | Chuỗi kết nối, thông tin SmartCA, HIS chỉ lấy từ biến môi trường/secret store; không commit; không ghi log |
| NFR-03 | Dữ liệu cá nhân | Số CCCD, SĐT, mã BHYT mã hóa ở mức cột; có giá trị băm để tra cứu; che khi hiển thị; tuân thủ quy định bảo vệ dữ liệu cá nhân (pháp chế xác nhận) |
| NFR-04 | Audit | Nhật ký chỉ ghi thêm, không có API sửa/xóa; dữ liệu nhạy cảm được che trong nhật ký |
| NFR-05 | Quy mô | 50–200 người dùng đồng thời, 500–3.000 phiếu/ngày |
| NFR-06 | Hiệu năng | Danh sách phiếu (trang 20 dòng) phản hồi < 1 giây ở quy mô trên; xuất 10.000 phiếu < 30 giây |
| NFR-07 | Lưu trữ | PDF khoảng 200–500 KB/phiếu, ước tính 200–550 GB/năm; chỉ giữ PDF cuối; có kế hoạch sao lưu |
| NFR-08 | Giao diện | Tiếng Việt, responsive cho máy tính bàn (quầy tiếp đón), máy tính bảng (tại giường) và điện thoại; không cuộn ngang |
| NFR-09 | Trình duyệt | Chrome/Edge bản hiện hành; camera cần HTTPS |
| NFR-10 | Thời gian | Lưu UTC, hiển thị giờ Việt Nam (UTC+7), định dạng `dd/MM/yyyy` |
| NFR-11 | Lỗi | API trả lỗi dạng ProblemDetails (RFC 9457) với thông điệp tiếng Việt và mã truy vết |
| NFR-12 | Vận hành | Health check `/health` (liveness), `/health/ready` (kết nối SQL Server); log có cấu trúc |
| NFR-13 | Triển khai | Máy chủ Windows Server tại bệnh viện (IIS hay Docker: chờ bệnh viện xác nhận); Docker Compose cho môi trường phát triển/kiểm thử |
| NFR-14 | Chất lượng | Build không cảnh báo; unit test, integration test, test kiến trúc; độ bao phủ Domain + Application ≥ 80% |

---

## 6. Tích hợp bên ngoài

| Hệ thống | Mục đích | Trạng thái |
|---|---|---|
| VNPT SmartCA (API SP769 v4.0) | Ký số cho mọi người ký | Thiết kế xong ([tich-hop-smartca.md](tich-hop-smartca.md)); chờ thông tin UAT và code mẫu |
| FPT eHospital | Tra cứu người bệnh, đợt khám, đối tượng | Chờ tài liệu API; dùng bản giả lập trước |

---

## 7. Giả định và việc còn mở

**Giả định:** tài khoản nội bộ (chưa SSO); PDF lưu trên thư mục của máy chủ, metadata và hash lưu trong SQL Server.

**Còn chờ bệnh viện:**
1. Thông tin kết nối SmartCA UAT và code mẫu VNPT.
2. Mẫu giấy 3 loại phiếu (đã xóa thông tin thật).
3. Tài liệu API FPT eHospital.
4. Hình thức chạy trên Windows Server (IIS hay Docker) và phiên bản SQL Server.
5. Danh sách lý do ký tay/ký thay, quan hệ được phép ký thay.
6. Số năm lưu trữ cụ thể theo quy định hồ sơ bệnh án.
