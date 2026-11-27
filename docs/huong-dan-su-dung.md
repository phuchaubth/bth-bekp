# Hướng dẫn sử dụng — Quản lý Phiếu đồng thuận

> Dành cho nhân viên bệnh viện: tiếp đón, điều dưỡng, bác sĩ, trưởng khoa, quản lý mẫu phiếu, báo cáo, kiểm tra, Phòng Điều dưỡng và quản trị hệ thống. Phần 1 dành cho mọi người. Các phần sau dành cho từng nhóm người dùng.
>
> Ảnh minh họa được chụp trên máy thử nghiệm với **dữ liệu giả lập**: tên người bệnh có chữ "(giả lập)", số CCCD, số điện thoại, số tiếp nhận đều là số thử. Tên đơn vị trên phiếu đang là giá trị mẫu. Màn hình của bạn có thể khác một chút: có logo và khung chữ ký của bệnh viện, hoặc ít mục menu hơn tùy vai trò.

## Mục lục

1. [Phần chung (mọi người)](#phan-chung): đăng nhập, đổi mật khẩu lần đầu, menu, trang Tổng quan, tài khoản và chữ ký mẫu, đăng xuất
2. [Tiếp đón, Điều dưỡng, Bác sĩ](#nhan-vien): người bệnh, lập phiếu, bộ phiếu, trình ký (trên máy bệnh viện hoặc mã QR cho điện thoại người bệnh), xác nhận, PDF, hủy, phiếu thay thế
3. [Người bệnh](#nguoi-benh): ký trên điện thoại (của bệnh viện hoặc của mình, quét mã QR), tự làm phiếu tại kiosk
4. [Trưởng khoa](#truong-khoa): thu hồi phiếu, báo cáo, xuất dữ liệu
5. [Quản lý mẫu phiếu](#quan-ly-mau-phieu): phiên bản, định nghĩa (JSON), cấu hình ký, phát hành
6. [Báo cáo / Xuất dữ liệu](#bao-cao): báo cáo thống kê, xuất Excel/CSV, lịch sử xuất
7. [Kiểm tra](#kiem-tra): nhật ký hệ thống, xem phiếu
8. [Quản trị hệ thống](#quan-tri): người dùng, vai trò và quyền, khoa/phòng, cấu hình hệ thống, chữ ký mẫu, bảo trì hệ thống
9. [Báo cáo điều dưỡng hằng ngày](#bao-cao-dieu-duong): điều dưỡng khoa lập và gửi báo cáo, ủy quyền khi vắng; Phòng Điều dưỡng tổng hợp, trả lại khoa, in PDF, xuất Excel, dashboard
10. [Kiểm thử tính năng mới](#kiem-thu): phân công kiểm thử theo người và thời gian, báo cáo kiểm thử, hướng xử lý
11. [Sổ nội kiểm đường huyết tại giường](#noi-kiem): khoa ghi kết quả từng ngày, Khoa HSH xem xét và xác nhận sổ tháng, in PDF theo mẫu
12. [Câu hỏi thường gặp](#hoi-dap)

### Một số từ dùng trong hướng dẫn

- **Phiếu**: một phiếu đồng thuận/cam kết của một người bệnh, lập theo một mẫu phiếu. Mỗi phiếu có số riêng, dạng `PDT-2026-000001`.
- **Bộ phiếu**: nhiều phiếu lập cùng lúc cho một người bệnh. Người bệnh ký **một lần** cho cả bộ.
- **Trình ký**: khóa nội dung phiếu để chuyển sang bước ký.
- **Người ký phía người bệnh**: người bệnh tự ký, hoặc người đại diện ký thay.
- **Xác nhận phía bệnh viện**: nhân viên đúng vai trò ký xác nhận sau khi người bệnh ký. Một phiếu có thể có nhiều bước xác nhận. Bước cuối cùng gọi là **xác nhận cuối** (nút **Xác nhận và hoàn tất**).
- **Lượt tiếp nhận**: lần người bệnh được tiếp nhận trên HIS (có số tiếp nhận). Chỉ dùng khi quản trị bật chức năng gắn phiếu với lượt tiếp nhận.
- **Chữ ký mẫu**: chữ ký của nhân viên, ký sẵn một lần, dùng lại khi ký xác nhận phiếu.

Trạng thái của phiếu:

| Trạng thái | Ý nghĩa |
|---|---|
| **Nháp** | Đang soạn, còn sửa được. |
| **Chờ ký** | Đã trình ký. Nội dung đã khóa. Đang chờ người bệnh ký hoặc chờ bệnh viện xác nhận. |
| **Hoàn tất** | Đủ chữ ký. Phiếu bị khóa, PDF đã ký được lưu. |
| **Đã hủy** | Phiếu nháp hoặc chờ ký đã bị hủy (có ghi lý do). |
| **Đã thu hồi** | Phiếu hoàn tất đã bị thu hồi (có ghi lý do). Có thể lập phiếu thay thế. |

---

<a id="phan-chung"></a>

## 1. Phần chung (mọi người)

### 1.1. Đăng nhập

1. Mở địa chỉ phần mềm do phòng CNTT cung cấp. Trên máy tính dùng Chrome hoặc Edge bản mới. Trên điện thoại dùng Safari (iPhone) hoặc Chrome (Android).
2. Nhập **Tên đăng nhập** và **Mật khẩu**.
3. Bấm **Đăng nhập**.

![Trang đăng nhập](huong-dan-su-dung/01-dang-nhap.png)

*Hình 1.1. Trang đăng nhập: nhập tên đăng nhập, mật khẩu rồi bấm **Đăng nhập**.*

Lưu ý:

- Nhập sai mật khẩu 5 lần thì tài khoản bị tạm khóa (mặc định 15 phút). Chờ hết thời gian, hoặc nhờ quản trị hệ thống mở khóa.
- Quên mật khẩu: liên hệ bộ phận CNTT để được đặt lại mật khẩu tạm.
- Khi phiên đăng nhập hết hạn, phần mềm đưa bạn về trang đăng nhập. Đăng nhập lại, phần mềm mở lại đúng trang bạn đang làm.
- Đầu trang có **dải vàng "UAT: môi trường kiểm thử"** (tab trình duyệt ghi `[UAT]`): bạn đang ở **môi trường kiểm thử**, dùng để thử bản mới trước khi đưa vào chạy thật (quyết định 50). Phiếu, báo cáo lập ở đây không có giá trị; không dùng cho người bệnh thật. Môi trường chạy thật không có dải này. Tài khoản và mật khẩu trên hai môi trường là riêng.

  ![Trang đăng nhập trên môi trường kiểm thử UAT: dải vàng UAT ở đầu trang](huong-dan-su-dung/uat-dang-nhap.png)

  *Hình 1.1a. Môi trường kiểm thử UAT: dải vàng ở đầu mọi trang, kể cả trang đăng nhập.*

### 1.2. Đổi mật khẩu lần đầu

Tài khoản mới, hoặc tài khoản vừa được quản trị đặt lại mật khẩu, phải đổi mật khẩu ngay sau khi đăng nhập. Trang **Đổi mật khẩu** tự hiện ra.

1. Nhập **Mật khẩu hiện tại** (mật khẩu tạm được cấp).
2. Nhập **Mật khẩu mới**, rồi nhập lại ở ô **Nhập lại mật khẩu mới**.
3. Bấm **Đổi mật khẩu**. Phần mềm chuyển sang trang **Tổng quan**.

<img src="huong-dan-su-dung/02-doi-mat-khau-lan-dau.png" alt="Trang đổi mật khẩu lần đầu" width="420">

*Hình 1.2. Đổi mật khẩu bắt buộc ở lần đăng nhập đầu tiên.*

Mật khẩu mới phải:

- dài từ 10 ký tự trở lên;
- có chữ hoa, chữ thường, chữ số và ký tự đặc biệt;
- không chứa tên đăng nhập.

Sau khi đổi mật khẩu, các phiên đăng nhập của bạn trên máy khác bị đăng xuất. Bạn có thể đổi mật khẩu bất cứ lúc nào ở **Tài khoản của tôi** (mục 1.5).

### 1.3. Giao diện và menu

Trên máy tính, thanh trên cùng có:

- bên trái: tên phần mềm **Phiếu đồng thuận** và khoa/phòng của bạn (tài khoản không thuộc khoa nào thì ghi **Toàn viện**);
- ở giữa: menu ngang;
- bên phải: tên của bạn. Bấm vào tên để mở **Tài khoản của tôi** hoặc **Đăng xuất**.

![Menu ngang của quản trị hệ thống, đang mở menu Quản trị](huong-dan-su-dung/03-menu-ngang.png)

*Hình 1.3. Menu ngang. Mục đang mở được gạch chân. Bấm **Quản trị** để mở các mục con.*

<img src="huong-dan-su-dung/04-menu-tai-khoan.png" alt="Menu tài khoản" width="233">

*Hình 1.4. Bấm vào tên của bạn để mở **Tài khoản của tôi** hoặc **Đăng xuất**.*

Mỗi người chỉ thấy các mục menu được phép dùng. Với phân quyền mặc định:

| Mục menu | Tiếp đón, Điều dưỡng, Bác sĩ | Trưởng khoa | Quản lý mẫu phiếu | Báo cáo / Xuất dữ liệu | Kiểm tra | Quản trị hệ thống |
|---|---|---|---|---|---|---|
| **Tổng quan** | có | có | có | có | có | có |
| **Phiếu đồng thuận** | có | có | chỉ xem | chỉ xem | chỉ xem | có |
| **Người bệnh** | có | có | không | chỉ xem | chỉ xem | có |
| **Mẫu phiếu** | chỉ xem | chỉ xem | có | chỉ xem | chỉ xem | có |
| **Xuất dữ liệu** | không | có | không | có | không | có |
| **Báo cáo** (Báo cáo thống kê) | không | có | không | có | có | có |
| **Nhật ký** (Nhật ký hệ thống) | không | không | không | không | có | có |
| **Điều dưỡng** (Báo cáo điều dưỡng, mục 9) | chỉ Điều dưỡng: **Báo cáo của khoa** | không | không | không | không | có |
| **Quản trị** | không | không | không | không | chỉ **Người dùng** (xem) | có |

Vai trò **Phòng Điều dưỡng** thấy **Tổng quan** và **Điều dưỡng** → **Dashboard** (bảng tổng hợp hằng ngày nằm trong Dashboard, mục 9.2), **Ủy quyền báo cáo**.

Quản trị hệ thống có thể đổi quyền của từng vai trò (mục 8.3), nên menu của bạn có thể khác bảng trên.

Trên điện thoại hoặc máy tính bảng, menu được thu gọn. Bấm nút ☰ (**Mở menu**) ở góc trên bên trái để mở danh sách đầy đủ.

<img src="huong-dan-su-dung/05-menu-dien-thoai.png" alt="Menu trên điện thoại" width="300">

*Hình 1.5. Menu trên điện thoại: bấm ☰ ở góc trên bên trái, chọn mục cần mở.*

### 1.4. Trang Tổng quan

Trang **Tổng quan** mở ra ngay sau khi đăng nhập. Muốn quay lại trang này, bấm **Tổng quan** trên menu hoặc bấm tên phần mềm ở góc trên bên trái.

![Trang Tổng quan của một điều dưỡng](huong-dan-su-dung/06-tong-quan.png)

*Hình 1.6. Trang Tổng quan: bốn ô số liệu, danh sách **Chờ tôi xác nhận**, **Phiếu tôi lập chưa hoàn tất** và biểu đồ 14 ngày.*

Trang có:

- **Phiếu nháp**, **Chờ ký**, **Hoàn tất hôm nay**, **Thu hồi 30 ngày qua**: số phiếu trong phạm vi bạn được xem (khoa của bạn và phiếu bạn lập; vai trò toàn viện thì cả bệnh viện).
- **Chờ tôi xác nhận**: phiếu người bệnh đã ký và **bước xác nhận đang chờ thuộc vai trò của bạn** (bước giữa hoặc bước cuối). Phiếu còn chờ một bước trước của vai trò khác chưa hiện ở đây; phiếu bạn đã xác nhận một bước cũng không hiện nữa (mỗi bước một người khác nhau). Bấm số phiếu để mở.
- **Phiếu tôi lập chưa hoàn tất**: phiếu bạn lập còn ở trạng thái nháp hoặc chờ ký.
- **14 ngày gần nhất**: số phiếu lập và số phiếu hoàn tất mỗi ngày.
- **Trạng thái hệ thống**: **Máy chủ đang hoạt động** nghĩa là phần mềm kết nối bình thường.

### 1.5. Tài khoản của tôi và chữ ký mẫu

Bấm vào tên của bạn ở góc trên bên phải, chọn **Tài khoản của tôi**. Trang có ba phần:

- **Thông tin tài khoản**: họ tên, tên đăng nhập, chức danh, khoa/phòng chính (và các khoa/phòng khác nếu bạn làm ở nhiều khoa), vai trò.
- **Chữ ký mẫu**: chữ ký của bạn, dùng khi ký xác nhận phiếu.
- **Đổi mật khẩu**: đổi mật khẩu như mục 1.2.

![Trang Tài khoản của tôi](huong-dan-su-dung/07-tai-khoan-cua-toi.png)

*Hình 1.7. Trang **Tài khoản của tôi**. Phần **Chữ ký mẫu** ở bên phải.*

Ký chữ ký mẫu (nên làm một lần trước khi bắt đầu xác nhận phiếu):

1. Trong phần **Chữ ký mẫu**, bấm **Ký chữ ký mẫu** (đã có chữ ký mẫu thì nút là **Ký lại chữ ký mẫu**).
2. Ký vào khung bằng ngón tay, bút cảm ứng hoặc chuột, như khi ký giấy tờ thường ngày. Ký hỏng thì bấm **Ký lại**.
3. Bấm **Lưu chữ ký mẫu**.

<img src="huong-dan-su-dung/08-ky-chu-ky-mau.png" alt="Hộp ký chữ ký mẫu" width="560">

*Hình 1.8. Ký chữ ký mẫu rồi bấm **Lưu chữ ký mẫu**.*

Khi đã có chữ ký mẫu, mỗi lần ký xác nhận phiếu bạn chỉ cần bấm nút xác nhận, không phải ký lại (xem mục 2.10). Muốn bỏ chữ ký mẫu, bấm **Xóa** rồi xác nhận. Phiếu đã ký giữ nguyên chữ ký đã ký.

### 1.6. Đăng xuất

1. Bấm vào tên của bạn ở góc trên bên phải.
2. Chọn **Đăng xuất**.

Máy dùng chung (ví dụ điện thoại ký phiếu của khoa): đăng xuất khi đổi ca để người sau đăng nhập bằng tài khoản của mình.

---

<a id="nhan-vien"></a>

## 2. Tiếp đón, Điều dưỡng, Bác sĩ

Ba vai trò này có cùng các chức năng: tìm và thêm người bệnh, lập phiếu, cho người bệnh ký, ký xác nhận phía bệnh viện. Bạn thấy phiếu của khoa mình và phiếu do chính mình lập. Tài khoản làm ở nhiều khoa (quản trị đặt ở mục 8.1) thấy phiếu của mọi khoa đó; khi lập phiếu, ô **Khoa của phiếu** mặc định là khoa chính.

Quy trình thường gặp:

1. Tìm người bệnh hoặc thêm người bệnh mới (mục 2.1–2.3).
2. Lập phiếu hoặc lập bộ phiếu, soạn nội dung, bấm **Trình ký** (mục 2.4–2.7).
3. Đưa máy cho người bệnh ký (mục 2.8; phía người bệnh xem phần 3).
4. Nhân viên đúng vai trò ký xác nhận; người xác nhận cuối bấm **Xác nhận và hoàn tất** (mục 2.9–2.11).
5. Xem hoặc in PDF đã ký (mục 2.12).

### 2.1. Tìm người bệnh

1. Bấm **Người bệnh** trên menu.
2. Gõ vào ô tìm kiếm: họ tên (gõ không dấu cũng được), mã người bệnh, hoặc số CCCD đủ 12 số. Nhấn Enter.
3. Bấm tên người bệnh để mở hồ sơ.

Danh sách chỉ có người bệnh **liên quan đến khoa của bạn**: có phiếu ở khoa của bạn, có phiếu bạn lập, hoặc hồ sơ bạn tiếp nhận. Người bệnh khoa khác chuyển đến (chưa có phiếu ở khoa bạn): tìm bằng **đúng mã người bệnh** hoặc **đủ 12 số CCCD** (hoặc quét CCCD), rồi lập phiếu; từ đó người bệnh có trong danh sách của khoa. Tài khoản có quyền xem toàn viện thấy mọi người bệnh.

![Danh sách người bệnh](huong-dan-su-dung/10-danh-sach-nguoi-benh.png)

*Hình 2.1. Danh sách người bệnh. Số CCCD được che bớt. Nút **Tiếp nhận người bệnh** ở góc phải để thêm người bệnh mới.*

### 2.2. Thêm người bệnh mới (Tiếp nhận người bệnh)

Bấm **Người bệnh** → **Tiếp nhận người bệnh**. Có ba cách điền thông tin.

**Cách 1: quét mã QR trên CCCD bằng máy quét 2D**

1. Bấm vào ô *"Đặt con trỏ vào đây rồi quét mã QR trên CCCD (máy quét 2D)"*.
2. Quét mã QR trên thẻ CCCD. Phần mềm tự điền họ tên, ngày sinh, giới tính, số CCCD, số CMND cũ, nơi thường trú, ngày cấp.
3. Kiểm tra lại thông tin. Bổ sung số điện thoại, dân tộc, nghề nghiệp, mã thẻ BHYT, mã người bệnh nếu có.
4. Bấm **Lưu hồ sơ**.

Không có máy quét: bấm **Quét bằng camera**. Lần đầu, trình duyệt hỏi quyền dùng camera: chọn **Cho phép**. Hộp thoại ghi *"Đang mở camera…"* cho tới khi camera bật. Đưa mã QR ở mặt trước thẻ vào **khung vuông** giữa khung hình, cách camera khoảng một gang tay, giữ yên 1–2 giây. Hình bị mờ thì đưa thẻ ra xa hơn một chút. Với webcam máy tính, hình được lật như soi gương: đưa thẻ sang trái thì hình cũng sang trái. Đọc được mã thì hộp thoại tự đóng và thông tin được điền như cách 1. Camera chỉ hoạt động khi phần mềm chạy qua địa chỉ an toàn (HTTPS).

![Tiếp nhận người bệnh sau khi quét CCCD](huong-dan-su-dung/11-tiep-nhan-quet-cccd.png)

*Hình 2.2. Sau khi quét CCCD: thông tin trên thẻ được điền sẵn. Khung xanh **Đã lấy thông tin từ hệ thống bệnh viện** chỉ có khi bệnh viện đã kết nối hệ thống thông tin người bệnh.*

Khi bệnh viện đã kết nối hệ thống thông tin người bệnh, sau khi quét thẻ, phần mềm tự lấy thêm thông tin còn thiếu (mã người bệnh, số điện thoại…). Phần mềm chỉ điền vào ô còn trống, thông tin trên thẻ được ưu tiên.

Máy quét chưa cài đọc tiếng Việt thường gõ chữ có dấu thành ký tự lạ (kiểu *"Ä‘Æ°á»ng"* thay cho *"đường"*). Phần mềm tự sửa lại các chữ này. Có máy quét bỏ hẳn chữ có dấu (kiểu *"ng"* thay cho *"Đường"*): chữ đã mất thì không sửa được. Địa chỉ không sửa được, hoặc bị mất chữ có dấu, thì phần mềm để trống, kèm cảnh báo *"Địa chỉ trong mã QR bị lỗi chữ có dấu…"*: nhập bổ sung. Nếu thấy khung vàng **Cần kiểm tra lại** (ví dụ *"Họ tên có ký tự lạ"*), hãy đối chiếu với thẻ và sửa lại, rồi báo bộ phận CNTT cài lại máy quét. Camera (**Quét bằng camera**) đọc đúng chữ có dấu trong mã QR.

**Cách 2: Lấy thông tin từ hệ thống bệnh viện**

Chỉ có khi bệnh viện đã kết nối hệ thống thông tin người bệnh. Khi đó, phần đầu trang có tên **1. Lấy thông tin người bệnh**.

1. Chọn **Mã người bệnh** hoặc **Số CCCD**.
2. Nhập mã hoặc 12 số CCCD.
3. Bấm **Lấy thông tin**.
4. Đọc khung **Đã lấy thông tin từ hệ thống bệnh viện**: khung cho biết ô nào còn phải nhập. Nhập phần còn thiếu rồi bấm **Lưu hồ sơ**.

![Lấy thông tin theo mã người bệnh](huong-dan-su-dung/13-lay-thong-tin.png)

*Hình 2.3. Lấy thông tin theo mã người bệnh: thông tin hành chính được điền sẵn vào phần **2. Thông tin người bệnh**.*

Nếu phần mềm báo không tìm thấy người bệnh, hoặc chưa kết nối được hệ thống thông tin người bệnh, hãy kiểm tra lại mã hoặc nhập trực tiếp (cách 3).

**Cách 3: nhập trực tiếp**

Dùng cho người bệnh không có CCCD (trẻ em, người nước ngoài…). Nhập thẳng vào phần **2. Thông tin người bệnh**, rồi bấm **Lưu hồ sơ**.

Các ô bắt buộc: **Họ và tên**, **Ngày sinh**, **Giới tính**. Phần mềm kiểm tra:

- **Số CCCD** gồm 12 chữ số và phải khớp năm sinh, giới tính;
- **Số điện thoại** đúng dạng, ví dụ 0912345678;
- **Mã thẻ BHYT** gồm 17 ký tự (2 chữ và 15 số). Mã thẻ cũ 15 ký tự vẫn lưu được nhưng có cảnh báo nhắc cập nhật sang mã mới;
- **Địa chỉ hiện tại**: để trống nếu giống nơi thường trú, hoặc bấm **Giống nơi thường trú** để chép.

**Người bệnh đã có hồ sơ**

Khi quét CCCD của người đã có hồ sơ, phần mềm báo **Người bệnh đã có hồ sơ trong hệ thống**:

- bấm **Cập nhật hồ sơ theo CCCD** để cập nhật hồ sơ cũ theo thông tin trên thẻ, rồi bấm **Lưu thay đổi**;
- hoặc bấm **Mở hồ sơ** để mở hồ sơ đã có.

![Cảnh báo người bệnh đã có hồ sơ](huong-dan-su-dung/12-da-co-ho-so.png)

*Hình 2.4. Quét CCCD của người đã có hồ sơ: chọn cập nhật hồ sơ cũ hoặc mở hồ sơ. Phần mềm không tạo hồ sơ trùng.*

### 2.3. Hồ sơ người bệnh và người đại diện

Hồ sơ người bệnh có ba phần:

- **Thông tin nhân thân**. Số CCCD, số điện thoại, mã thẻ BHYT được che bớt. Bấm biểu tượng con mắt bên cạnh để xem đầy đủ. Bấm **Sửa thông tin** để sửa.
- **Người đại diện / người ký thay**: người ký thay cho người bệnh (cha mẹ của trẻ em, người giám hộ…).
- **Phiếu đồng thuận**: các phiếu của người bệnh, và nút **Lập phiếu**.

![Hồ sơ người bệnh có người đại diện](huong-dan-su-dung/14-ho-so-nguoi-benh.png)

*Hình 2.5. Hồ sơ người bệnh: thông tin nhân thân, người đại diện và danh sách phiếu.*

Thêm người đại diện:

1. Bấm **Thêm người đại diện**.
2. Quét CCCD của người đại diện vào ô quét ở đầu hộp (hoặc nhập tay).
3. Chọn **Quan hệ với người bệnh**: Cha, Mẹ, Vợ/Chồng, Con, Anh/chị/em ruột, Người giám hộ, Người thân khác, Khác.
4. Nhập số điện thoại nếu có, rồi bấm **Lưu**.

<img src="huong-dan-su-dung/15-them-nguoi-dai-dien.png" alt="Thêm người đại diện" width="620">

*Hình 2.6. Hộp **Thêm người đại diện / người ký thay**: thông tin được điền sẵn sau khi quét CCCD của người đại diện; chọn quan hệ rồi bấm **Lưu**.*

### 2.4. Lập phiếu

1. Bấm **Phiếu đồng thuận** → **Lập phiếu**. (Hoặc mở hồ sơ người bệnh, bấm **Lập phiếu**: người bệnh được chọn sẵn.)
2. Ở phần **1. Người bệnh**, chọn người bệnh:
   - gõ họ tên, mã người bệnh hoặc số CCCD vào ô tìm, rồi bấm **Chọn** ở dòng đúng người;
   - hoặc dùng **Lấy từ hệ thống bệnh viện** (khi bệnh viện đã kết nối): nhập mã người bệnh hoặc số CCCD, bấm **Lấy thông tin**. Người đã có hồ sơ được chọn ngay. Người chưa có hồ sơ: hộp **Lưu hồ sơ người bệnh** mở ra với thông tin điền sẵn; nhập phần còn thiếu rồi bấm **Lưu và chọn người bệnh**;
   - hoặc bấm **Tiếp nhận người bệnh mới** để thêm người bệnh như mục 2.2. Lưu xong (nút **Lưu và lập phiếu**), phần mềm quay lại trang lập phiếu.
3. Ở phần **2. Mẫu phiếu**, kiểm tra **Khoa của phiếu**. Mặc định là khoa của bạn. Chọn khoa đang điều trị người bệnh: nhân viên khoa đó (ví dụ điều dưỡng khoa) sẽ thấy phiếu để xác nhận. Bạn vẫn thấy phiếu mình lập.
4. Bấm **Lập phiếu** ở dòng mẫu cần dùng. Phần mềm tạo phiếu nháp và mở trang soạn nội dung (mục 2.6).

![Bước chọn người bệnh khi lập phiếu](huong-dan-su-dung/16-lap-phieu-chon-nguoi-benh.png)

*Hình 2.7. Lập phiếu, bước 1: lấy thông tin từ hệ thống bệnh viện, hoặc tìm hồ sơ đã có rồi bấm **Chọn**.*

<img src="huong-dan-su-dung/17-luu-nhanh-ho-so.png" alt="Lưu nhanh hồ sơ người bệnh" width="700">

*Hình 2.8. Người bệnh chưa có hồ sơ: kiểm tra thông tin điền sẵn, nhập phần còn thiếu, bấm **Lưu và chọn người bệnh**.*

Chỉ các mẫu đang phát hành mới có trong danh sách. Không có mẫu nào cho khoa đã chọn thì phần mềm báo *"Chưa có mẫu phiếu nào đang phát hành cho khoa này."*

### 2.5. Lập bộ phiếu (người bệnh ký một lần cho cả bộ)

Khi khoa có từ 2 mẫu trở lên, mỗi mẫu có ô đánh dấu. Mặc định tất cả các mẫu được chọn. Máy nhớ lựa chọn lần trước của bạn theo từng khoa.

1. Đánh dấu các mẫu cần làm cho người bệnh.
2. Bấm **Lập bộ phiếu (… mẫu)**.
3. Phần mềm mở phiếu đầu tiên của bộ. Soạn nội dung rồi bấm **Trình ký**: phần mềm tự chuyển sang phiếu nháp tiếp theo.
4. Lặp lại đến khi trình ký hết các phiếu.

![Chọn mẫu và lập bộ phiếu](huong-dan-su-dung/18-chon-mau-lap-bo-phieu.png)

*Hình 2.9. Lập phiếu, bước 2: chọn **Khoa của phiếu**, đánh dấu các mẫu, bấm **Lập bộ phiếu (4 mẫu)**. Muốn lập một phiếu lẻ, bấm **Lập phiếu** ở dòng mẫu đó.*

Trên mỗi phiếu của bộ có thanh **Bộ phiếu của … (… phiếu)**: liệt kê các phiếu, trạng thái từng phiếu. Bấm tên phiếu để chuyển nhanh.

### 2.6. Soạn nội dung phiếu và trình ký

![Trang soạn phiếu nháp](huong-dan-su-dung/19-soan-phieu.png)

*Hình 2.10. Phiếu nháp: chọn **Người ký phía người bệnh**, điền nội dung, rồi bấm **Lưu nháp** hoặc **Trình ký** ở góc phải phần **Nội dung phiếu**.*

1. Ở **Người ký phía người bệnh**, chọn:
   - **Người bệnh tự ký**; hoặc
   - **Người đại diện ký thay**, rồi chọn người đại diện trong danh sách. Lựa chọn này chỉ bấm được khi hồ sơ người bệnh đã có người đại diện (thêm như mục 2.3).
2. Điền nội dung phiếu:
   - một số ô được điền sẵn từ hồ sơ người bệnh (ví dụ dân tộc, nghề nghiệp, địa chỉ, số thẻ BHYT). Bạn vẫn sửa được;
   - một số ô do phần mềm tự tính (ví dụ BMI), không nhập tay;
   - một số câu chỉ hiện khi chọn một câu trả lời nhất định (ví dụ chọn "Có" thì phải ghi rõ).
3. Bấm **Lưu nháp** để lưu dở, làm tiếp sau.
4. Kiểm tra xong, bấm **Trình ký**. Nội dung phiếu và thông tin người bệnh được khóa, phiếu chuyển sang **Chờ ký**.

Nếu còn thiếu thông tin, phần mềm báo *"Còn … mục chưa đạt yêu cầu; xem các ô được đánh dấu đỏ."* Sửa các ô đỏ rồi bấm **Trình ký** lại.

<img src="huong-dan-su-dung/20-chon-nguoi-dai-dien.png" alt="Chọn người đại diện ký thay" width="800">

*Hình 2.11. Người bệnh là trẻ em: chọn **Người đại diện ký thay** và chọn người đại diện trong hồ sơ.*

Bấm **Xem PDF** bất cứ lúc nào để xem trước bản in. Phiếu chưa hoàn tất có dòng *"BẢN XEM TRƯỚC – CHƯA CÓ CHỮ KÝ"*.

### 2.7. Kế thừa phiếu lần trước

Chức năng này chỉ chạy khi quản trị đã đặt số ngày ở **Cấu hình hệ thống → Kế thừa phiếu lần trước** (mục 8.5).

Khi người bệnh làm lại **cùng mẫu phiếu**, **ở cùng khoa**, trong số ngày đó, phiếu mới được điền sẵn câu trả lời của phiếu **đã hoàn tất** gần nhất. Áp dụng cả khi lập phiếu lẻ và lập bộ phiếu.

- Trên trang phiếu nháp có khung vàng *"Đã điền sẵn theo phiếu … (hoàn tất …): kiểm tra lại trước khi trình ký."* Khung còn đó khi mở lại phiếu hoặc tải lại trang, cho đến khi trình ký. Với bộ phiếu, thanh bộ phiếu cũng ghi phiếu nào được điền sẵn.
- Hãy kiểm tra lại từng câu trước khi bấm **Trình ký**.
- Không kế thừa: ý kiến Đồng ý/Không đồng ý, lời xác nhận và chữ ký. Người bệnh vẫn chọn và ký lại.
- Phiếu nháp, phiếu đã hủy, phiếu đã thu hồi không được dùng để điền sẵn.

![Thông báo đã điền sẵn theo phiếu lần trước](huong-dan-su-dung/21-ke-thua-phieu.png)

*Hình 2.12. Khung vàng trên phiếu nháp: phiếu mới được điền sẵn theo phiếu PDT-2026-000001 đã hoàn tất.*

### 2.8. Trình ký: cho người bệnh ký trên điện thoại

Sau khi trình ký, phiếu ở trạng thái **Chờ ký** và có khung *"Phiếu đang chờ ký"*.

![Bộ phiếu đang chờ ký](huong-dan-su-dung/22-bo-phieu-cho-ky.png)

*Hình 2.13. Cả bộ phiếu đã trình ký: nút **Ký cả bộ trên điện thoại (4 phiếu)** ở thanh bộ phiếu.*

Có hai cách cho người bệnh ký:

- **Cách 1: trên máy của bệnh viện.** Người bệnh ký ngay trên máy bạn đang dùng (điện thoại, máy tính bảng hoặc máy tính). Bạn mở chế độ ký rồi đưa máy cho người bệnh.
- **Cách 2: trên điện thoại của người bệnh (mã QR).** Bạn hiện mã QR trên màn hình; người bệnh quét bằng điện thoại của mình, nhập ngày sinh rồi ký. Máy của bạn không phải chuyển chế độ (xem phần **Cách 2** bên dưới).

#### Cách 1: ký trên máy của bệnh viện

1. Mở phiếu trên máy sẽ đưa cho người bệnh (thường là điện thoại ký phiếu của khoa).
2. Bấm:
   - **Ký trên điện thoại** nếu là phiếu lẻ;
   - **Ký cả bộ trên điện thoại (… phiếu)** nếu là bộ phiếu. Khi đó người bệnh ký một lần cho mọi phiếu đang chờ ký của bộ.
3. Đọc hộp thông báo, bấm **Bắt đầu ký**.
4. Máy chuyển sang màn hình ký, không còn menu. **Phiên đăng nhập của bạn trên máy này được đóng.** Đưa máy cho người bệnh (hoặc người đại diện). Cách ký: xem phần 3.1.
5. Người bệnh ký xong và đưa máy lại. Bấm **Nhân viên: thoát chế độ ký** nếu khung đăng nhập chưa hiện. Nhập mật khẩu của bạn ở khung **Nhân viên đăng nhập để thoát chế độ ký**, bấm **Đăng nhập và thoát chế độ ký**. Máy mở lại trang phiếu.

<img src="huong-dan-su-dung/23-ky-ca-bo-hop-thoai.png" alt="Hộp Ký cả bộ trên điện thoại" width="520">

*Hình 2.14. Hộp **Ký cả bộ trên điện thoại**: bấm **Bắt đầu ký** để chuyển máy sang màn hình ký.*

<img src="huong-dan-su-dung/24-chuyen-che-do-ky.png" alt="Hộp chuyển sang chế độ ký trên điện thoại" width="300">

*Hình 2.15. Phiếu lẻ: hộp **Chuyển sang chế độ ký** trên điện thoại.*

Lưu ý:

- Nên bật khóa ứng dụng của điện thoại trước khi đưa máy: **Truy cập được hướng dẫn** (Guided Access) trên iPhone, **Ghim ứng dụng** trên Android. Người bệnh sẽ không thoát được ra ngoài trình duyệt.
- Màn hình ký chỉ nhận chữ ký cho đúng phiếu (hoặc bộ phiếu) đó. Không ai làm được việc khác trên máy cho đến khi nhân viên đăng nhập lại.
- Phiên ký có thời hạn (mặc định 30 phút). Nếu tải lại trang hoặc phiên hết hạn, màn hình báo *"Không có phiên ký đang mở"* hoặc *"Phiên ký đã kết thúc"*. Bấm **Đăng nhập**, mở lại phiếu và bấm **Ký trên điện thoại** (hoặc **Ký cả bộ trên điện thoại**) lần nữa.
- Bộ phiếu chỉ ký chung được khi các phiếu chờ ký **cùng một người ký** (cùng là người bệnh tự ký, hoặc cùng một người đại diện). Nếu khác nhau, thanh bộ phiếu báo không ký chung được. Hãy trả phiếu về nháp để chọn cùng người ký, hoặc ký riêng từng phiếu bằng nút **Ký trên điện thoại**.
- Phiếu còn nháp không có trong phiên ký của cả bộ. Thanh bộ phiếu nhắc *"Còn … phiếu nháp chưa trình ký…"*.

#### Cách 2: người bệnh ký trên điện thoại của mình (mã QR)

Dùng khi người bệnh muốn ký trên điện thoại riêng, hoặc khi không tiện đưa máy của bệnh viện. Điện thoại của người bệnh phải vào được địa chỉ của phần mềm (ví dụ bắt Wi-Fi của bệnh viện; xem với phòng CNTT).

1. Mở phiếu (hoặc phiếu bất kỳ trong bộ) đang chờ ký. Bấm:
   - **Mã QR cho người bệnh** nếu là phiếu lẻ;
   - **Mã QR ký cả bộ** trên thanh bộ phiếu nếu là bộ phiếu. Người bệnh ký một lần cho mọi phiếu đang chờ ký của bộ.

   ![Nút Mã QR cho người bệnh](huong-dan-su-dung/25-ma-qr-nut.png)

   *Hình 2.15a. Phiếu đang chờ ký: nút **Mã QR cho người bệnh** cạnh nút **Ký trên điện thoại**.*

2. Đọc hộp thông báo, bấm **Tạo mã QR**. Mã QR hiện ra.
3. Mời người bệnh (hoặc người đại diện) mở camera điện thoại, quét mã, chạm vào đường dẫn hiện ra. Cách làm trên điện thoại: xem phần 3.1.

   <img src="huong-dan-su-dung/38-ma-qr-hop-thoai.png" alt="Hộp mã QR cho người bệnh quét" width="320">

   *Hình 2.15b. Mã QR dùng được 15 phút nếu chưa quét, mở được trên **một** điện thoại.*

4. Hộp tự cập nhật: *"Đã mở trên điện thoại lúc …"* khi người bệnh đã nhập đúng ngày sinh, rồi *"Người bệnh đã ký xong"*. Bấm **Xong**. Trang phiếu hiện chữ ký của người bệnh; bạn xác nhận phiếu như thường lệ (mục 2.10).

   <img src="huong-dan-su-dung/39-ma-qr-da-ky.png" alt="Người bệnh đã ký xong trên điện thoại" width="320">

   *Hình 2.15c. Người bệnh đã ký xong trên điện thoại của mình.*

Lưu ý:

- Máy của bạn vẫn giữ phiên đăng nhập, không chuyển sang chế độ ký. Bạn có thể bấm **Đóng** để làm việc khác; mã vẫn dùng được đến giờ ghi trên hộp.
- Muốn dừng (ví dụ người bệnh đổi ý, muốn ký trên máy của bệnh viện): bấm **Hủy mã**. Điện thoại đã mở mã cũng không ký được nữa. Mở phiên ký khác (tạo mã mới, hoặc bấm **Ký trên điện thoại**) thì mã cũ hết hiệu lực.
- Người bệnh nhập sai ngày sinh 5 lần thì mã bị hủy: kiểm tra lại ngày sinh trong hồ sơ người bệnh, rồi bấm **Tạo mã mới**.
- Mỗi lần tạo mã, mở mã (kèm địa chỉ IP và trình duyệt của điện thoại), nhập sai ngày sinh, hủy mã đều được ghi nhật ký hệ thống.

### 2.9. Sau khi người bệnh ký

Phiếu vẫn ở trạng thái **Chờ ký** nhưng có khung xanh *"… đã ký lúc …"*. Khung cho biết:

- **Đã có …/… chữ ký**: số chữ ký đã có trên tổng số chữ ký cần có (người bệnh, các bước xác nhận, xác nhận cuối);
- danh sách các bước xác nhận: bước nào đã xác nhận (ai, lúc nào), bước nào **đang chờ**, bước nào **chưa đến lượt**;
- việc bạn cần làm tiếp, ví dụ *"Bấm “Xác nhận: …” và ký vào khung để xác nhận phần của bạn."*

![Phiếu nhiều bước xác nhận đang chờ điều dưỡng](huong-dan-su-dung/26-cho-xac-nhan-nhieu-buoc.png)

*Hình 2.16. Phiếu có 3 bước phía bệnh viện: bước 1 đã xác nhận, bước 2 (điều dưỡng) đang chờ. Nút **Xác nhận: Điều dưỡng thực hiện (Nurse in charge)** ở đầu trang.*

### 2.10. Xác nhận phía bệnh viện

Mỗi mẫu phiếu quy định ai xác nhận. Với các mẫu hiện có:

| Mẫu phiếu | Người bệnh chọn ý kiến? | Các bước xác nhận phía bệnh viện |
|---|---|---|
| QT-KNGT-BM02 – Giấy đồng thuận về chế độ thanh toán và quyền lợi khám chữa bệnh BHYT | Không, chỉ ký | Tiếp đón (xác nhận cuối) |
| TTNB-NGOAITRU – Phiếu thông tin người bệnh ngoại trú | Không, chỉ ký | Tiếp đón (xác nhận cuối) |
| QT-KNGT-BM10 – Phiếu nhận định ban đầu và nhu cầu giáo dục sức khỏe người bệnh ngoại trú | Không, chỉ ký | Tiếp đón → Điều dưỡng → Bác sĩ (xác nhận cuối) |
| TUVAN-CONGTT – Phiếu tư vấn tham gia và sử dụng cổng thông tin chính thức của bệnh viện | Có: Đồng ý (Agree) / Không đồng ý (Disagree) | Người tư vấn (xác nhận cuối; mặc định vai trò Điều dưỡng, đổi ở *Sửa thông tin mẫu*) |

Người quản lý mẫu phiếu có thể đổi cấu hình này (phần 5). Khung trên trang phiếu luôn cho biết bước nào đang chờ, vai trò nào cần xác nhận.

Điều kiện để thấy nút xác nhận:

- bạn có đúng vai trò của bước đang chờ;
- bạn thấy được phiếu (phiếu thuộc khoa của bạn, hoặc do bạn lập);
- mỗi bước một người khác nhau: một tài khoản không xác nhận hai phần trên cùng một phiếu;
- các bước trước đã xác nhận xong. Bước xác nhận cuối chỉ mở khi mọi bước giữa đã xong.

Các bước xác nhận:

1. Mở phiếu. Bấm **Xác nhận: <tên bước>** (bước giữa) hoặc **Xác nhận và hoàn tất** (bước cuối).
2. Ký trong hộp vừa mở:
   - nếu đã có chữ ký mẫu: hộp hiện **Ký bằng chữ ký mẫu của bạn**. Không cần ký lại. Muốn ký tay riêng lần này, bấm **Ký tay lần này**;
   - nếu chưa có chữ ký mẫu: ký vào khung bằng ngón tay, bút cảm ứng hoặc chuột. Nút xác nhận chỉ bấm được sau khi đã ký.
3. Nếu là bước cuối và quản trị đã bật gắn phiếu với lượt tiếp nhận: chọn lượt tiếp nhận (mục 2.11).
4. Bấm **Ký và xác nhận** (bước giữa) hoặc **Ký và hoàn tất** (bước cuối).

<img src="huong-dan-su-dung/27-ky-bang-chu-ky-mau.png" alt="Ký xác nhận bằng chữ ký mẫu" width="640">

*Hình 2.17. Ký xác nhận bằng chữ ký mẫu: bấm **Ký và hoàn tất** (bước giữa là **Ký và xác nhận**). Bấm **Ký tay lần này** nếu muốn ký tay.*

Chữ ký của bạn được in vào ô của bạn trên phiếu, kèm họ tên và thời điểm theo tài khoản đang đăng nhập.

**Xác nhận cả bộ.** Với bộ phiếu, bấm **Xác nhận cả bộ (… phiếu)** trên thanh bộ phiếu. Bạn ký một lần cho mọi phiếu đang đến lượt mình. Phiếu nào còn bước giữa thì được xác nhận bước đó. Phiếu nào đến bước cuối thì được hoàn tất. Bấm **Ký và xác nhận cả bộ**.

<img src="huong-dan-su-dung/28-xac-nhan-ca-bo.png" alt="Hộp xác nhận cả bộ phiếu" width="700">

*Hình 2.18. **Xác nhận cả bộ phiếu**: hộp liệt kê các phiếu đến lượt bạn. Chọn lượt tiếp nhận (nếu có), ký vào khung, rồi bấm **Ký và xác nhận cả bộ** ở cuối hộp.*

### 2.11. Chọn lượt tiếp nhận khi ký hoàn tất

Phần này chỉ có khi quản trị đã bật **Gắn phiếu với lượt tiếp nhận (HIS)** ở **Cấu hình hệ thống**. Khi đó, hộp ký hoàn tất (và hộp **Xác nhận cả bộ** khi có phiếu được hoàn tất) có bảng **Lượt tiếp nhận hôm nay (bắt buộc)**.

1. Xem bảng các lượt tiếp nhận của người bệnh trên HIS, mới nhất ở trên. Mỗi dòng có thời gian tiếp nhận, số tiếp nhận, nơi tiếp nhận, người tiếp nhận và **Các phiếu đã gắn vào** lượt đó.
2. Bấm vào dòng đúng lượt tiếp nhận mà phiếu thuộc về.
3. Phiếu thuộc lượt của ngày khác: bấm **Chọn ngày khác**, chọn từ ngày đến ngày. Bấm **Hôm nay** để quay về danh sách hôm nay. Bấm nút làm mới (biểu tượng mũi tên tròn) để tải lại danh sách.
4. Ký rồi bấm **Ký và hoàn tất** (hoặc **Ký và xác nhận cả bộ**).

<img src="huong-dan-su-dung/29-hoan-tat-chon-luot.png" alt="Ký hoàn tất có chọn lượt tiếp nhận và chữ ký mẫu" width="700">

*Hình 2.19. Ký hoàn tất: dòng lượt tiếp nhận đã chọn được tô màu. Cột **Các phiếu đã gắn vào** giúp tránh chọn nhầm lượt.*

<img src="huong-dan-su-dung/30-hoan-tat-ky-tay.png" alt="Ký hoàn tất khi chưa có chữ ký mẫu" width="700">

*Hình 2.20. Tài khoản chưa có chữ ký mẫu: chọn lượt tiếp nhận rồi ký vào khung bên dưới. Nút **Ký và hoàn tất** ở cuối hộp chỉ bấm được khi đã chọn lượt và đã ký.*

Lưu ý:

- Hồ sơ người bệnh cần có mã người bệnh hoặc số CCCD để tra lượt tiếp nhận.
- Số tiếp nhận, thời gian, nơi tiếp nhận và người tiếp nhận được in ở chân mỗi trang PDF.
- Bảng báo lỗi (không lấy được danh sách từ HIS): bấm **Thử lại**. Nếu chưa khắc phục ngay được, báo quản trị tạm tắt chức năng này. Sau khi tắt, bấm **Thử lại** là ký hoàn tất được (phiếu hoàn tất lúc tắt không gắn lượt tiếp nhận).
- Hôm nay chưa có lượt nào và phiếu thuộc lượt trước: bấm **Chọn ngày khác**.

### 2.12. Phiếu đã hoàn tất, xem và in PDF

Sau bước xác nhận cuối, phiếu chuyển sang **Hoàn tất**: phiếu bị khóa, không sửa được, PDF đã ký được lưu kèm mã băm (SHA-256) để phát hiện nếu file bị sửa.

![Phiếu đã hoàn tất](huong-dan-su-dung/31-phieu-hoan-tat.png)

*Hình 2.21. Phiếu hoàn tất: dòng **Lượt tiếp nhận** (khi có gắn lượt) và khung **Phiếu đã hoàn tất**. Nút **Xem PDF đã ký** ở góc phải.*

Phần **Lịch sử phiếu** ở cuối trang ghi: ai lập phiếu, lúc trình ký, người bệnh ký (kèm lời xác nhận), từng nhân viên ký xác nhận, lúc hoàn tất, và mã SHA-256 của PDF đã ký.

![Lịch sử phiếu](huong-dan-su-dung/32-lich-su-phieu.png)

*Hình 2.22. **Lịch sử phiếu**: lời xác nhận của người bệnh in đỏ; dưới cùng là mã SHA-256 của PDF đã ký.*

Xem và in PDF:

1. Bấm **Xem PDF** (phiếu chưa hoàn tất) hoặc **Xem PDF đã ký** (phiếu hoàn tất).
2. PDF hiện trong cửa sổ. Dùng nút in hoặc tải trên thanh công cụ của PDF, hoặc bấm **Tải PDF** ở cuối cửa sổ rồi in file đã tải.

![Cửa sổ xem PDF](huong-dan-su-dung/33-xem-pdf.png)

*Hình 2.23. Cửa sổ xem PDF: thanh công cụ của PDF có nút tải và in. Nút **Tải PDF** ở góc dưới bên phải.*

<img src="huong-dan-su-dung/34-pdf-trang-chu-ky.png" alt="Trang chữ ký trên PDF đã ký" width="620">

*Hình 2.24. Phần chữ ký trên PDF: chữ ký người bệnh kèm lời xác nhận (chữ đỏ), dòng "Ký chung một lần cho 4 phiếu", chữ ký từng nhân viên. Chân trang có số phiếu và lượt tiếp nhận (phần giữa trang trống đã được lược bớt).*

### 2.13. Trả về nháp, hủy phiếu

**Trả về nháp** (phiếu đang **Chờ ký**, chưa hoàn tất): dùng khi cần sửa nội dung.

1. Bấm **Trả về nháp**, rồi bấm **Trả về nháp** trong hộp hỏi lại.
2. Nếu người bệnh đã ký, chữ ký của người bệnh bị bỏ. Sau khi sửa, phải trình ký và ký lại.

**Hủy phiếu** (phiếu **Nháp** hoặc **Chờ ký**): dùng khi phiếu lập nhầm, không dùng nữa.

1. Bấm **Hủy phiếu**.
2. Nhập lý do (ít nhất 5 ký tự).
3. Bấm **Hủy phiếu** trong hộp.

<img src="huong-dan-su-dung/35-huy-phieu.png" alt="Hộp hủy phiếu" width="520">

*Hình 2.25. Hủy phiếu: phải ghi lý do. Phiếu đã hủy không dùng được nữa; muốn làm lại thì lập phiếu mới.*

Phiếu đã **Hoàn tất** không trả về nháp, không hủy được. Muốn thay đổi, trưởng khoa (hoặc quản trị) thu hồi phiếu, rồi lập phiếu thay thế (mục 4.1, 4.2).

### 2.14. Danh sách phiếu

Bấm **Phiếu đồng thuận** trên menu để xem các phiếu bạn được phép xem.

- Tìm theo số phiếu, họ tên, mã người bệnh.
- Lọc theo **Trạng thái**, **Mẫu phiếu**, khoảng ngày lập (**Từ ngày** – **Đến ngày**).
- Bấm số phiếu (hoặc bấm đúp vào dòng) để mở phiếu.

![Danh sách phiếu đồng thuận](huong-dan-su-dung/36-danh-sach-phieu.png)

*Hình 2.26. Danh sách phiếu: bộ lọc ở trên, cột **Trạng thái** có màu để dễ phân biệt.*

### 2.15. Phiếu người bệnh tự làm tại kiosk

Phiếu người bệnh tự làm tại kiosk (phần 3.2) đã có chữ ký người bệnh, thuộc khoa đặt máy kiosk và chờ nhân viên xác nhận. Trên thanh bộ phiếu và trang phiếu có nhãn:

- **Lập tại kiosk · quét CCCD** hoặc **Lập tại kiosk · nhập mã y tế/CCCD**: đối chiếu CCCD của người bệnh trước khi xác nhận;
- **Lập tại kiosk · tự khai** (màu cam): người bệnh tự khai, chưa đối chiếu hồ sơ. Kiểm tra kỹ giấy tờ tùy thân và thông tin hành chính trước khi xác nhận.

Kiểm tra xong, bấm **Xác nhận cả bộ (… phiếu)** như mục 2.10.

![Bộ phiếu lập tại kiosk](huong-dan-su-dung/37-phieu-tu-kiosk.png)

*Hình 2.27. Bộ phiếu do người bệnh tự làm tại kiosk: nhãn **Lập tại kiosk · quét CCCD** và lời nhắc đối chiếu CCCD.*

---

<a id="nguoi-benh"></a>

## 3. Người bệnh

Phần này mô tả màn hình người bệnh (hoặc người đại diện) nhìn thấy. Nhân viên nên đọc để hướng dẫn người bệnh.

### 3.1. Ký trên điện thoại

Nhân viên mở chế độ ký (mục 2.8) rồi đưa máy cho người bệnh, hoặc hiện mã QR để người bệnh ký trên điện thoại của mình. Màn hình ký không có menu. Có thể cầm dọc. Xoay ngang thì khung ký rộng hơn.

**Ký trên điện thoại của mình (quét mã QR)**

1. Mở camera điện thoại, quét mã QR trên màn hình của nhân viên, chạm vào đường dẫn hiện ra.
2. Nhập **ngày sinh của người bệnh** (người đại diện ký thay cũng nhập ngày sinh của người bệnh), ví dụ gõ 12031985 cho 12/03/1985. Chạm **Mở phiếu**. Nhập sai thì màn hình báo còn mấy lần thử; sai 5 lần thì mã bị hủy, nhờ nhân viên tạo mã mới.
3. Đọc phiếu, chọn ý kiến (nếu phiếu có hỏi), ghi lời xác nhận và ký như hướng dẫn bên dưới.
4. Màn hình *"Đã ghi nhận chữ ký"* hiện ra là xong. Có thể đóng trang. Mã chỉ dùng một lần; điện thoại khác quét lại mã cũng không mở được.

<p>
<img src="huong-dan-su-dung/48-dt-rieng-ngay-sinh.png" alt="Điện thoại người bệnh: nhập ngày sinh để mở phiếu" width="200">
<img src="huong-dan-su-dung/48-dt-rieng-noi-dung.png" alt="Điện thoại người bệnh: nội dung phiếu" width="200">
<img src="huong-dan-su-dung/49-dt-rieng-ky-ten.png" alt="Điện thoại người bệnh: lời xác nhận và chữ ký" width="200">
<img src="huong-dan-su-dung/49-dt-rieng-ky-xong.png" alt="Điện thoại người bệnh: đã ký xong" width="200">
</p>

*Hình 3.1a. Trên điện thoại của người bệnh, từ trái sang: nhập ngày sinh; đọc phiếu; ghi lời xác nhận và ký; đã ký xong. Lỡ tải lại trang thì phiếu mở lại, không phải nhập lại ngày sinh.*

**Ký một bộ phiếu (nhiều phiếu, ký một lần)**

1. Màn hình **Ký … phiếu một lần** liệt kê **Các phiếu cần ký**. Chạm **Xem nội dung phiếu** để đọc từng phiếu. Chạm lần nữa để thu gọn.
2. Phiếu nào có câu hỏi ý kiến thì chọn câu trả lời, ví dụ **Đồng ý (Agree)** hoặc **Không đồng ý (Disagree)**.
3. Ở phần **Chữ ký của người bệnh cho cả … phiếu**, ghi lời xác nhận: chạm một câu gợi ý (ví dụ *"Tôi đã nghe, hiểu và đồng ý"*), hoặc tự viết vào ô bên dưới. Ô có dấu * đỏ là bắt buộc.
4. Ký vào khung bằng ngón tay hoặc bút cảm ứng. Ký hỏng thì chạm **Ký lại**.
5. Chạm **Xác nhận chữ ký cho cả … phiếu**. Nút chỉ chạm được khi đã chọn đủ ý kiến, đã ghi lời xác nhận (nếu bắt buộc) và đã ký. Dòng chữ dưới nút cho biết còn thiếu gì.
6. Màn hình báo **Đã ký xong … phiếu**. Đưa máy lại cho nhân viên.

<p>
<img src="huong-dan-su-dung/40-ky-ca-bo-dien-thoai.png" alt="Danh sách phiếu cần ký trên điện thoại" width="260">
<img src="huong-dan-su-dung/41-xem-noi-dung-dien-thoai.png" alt="Xem nội dung một phiếu trên điện thoại" width="260">
</p>

*Hình 3.1. Trái: danh sách phiếu cần ký. Phải: chạm **Xem nội dung phiếu** để đọc nội dung (chỉ đọc).*

<p>
<img src="huong-dan-su-dung/42-chon-y-kien-dien-thoai.png" alt="Chọn ý kiến Đồng ý hoặc Không đồng ý" width="260">
<img src="huong-dan-su-dung/43-ky-ten-dien-thoai.png" alt="Lời xác nhận và khung ký trên điện thoại" width="260">
</p>

*Hình 3.2. Trái: phiếu có hỏi ý kiến, đã chọn **Đồng ý (Agree)**. Phải: chọn lời xác nhận (hiện chữ đỏ trong ô), ký vào khung, chạm **Xác nhận chữ ký cho cả 2 phiếu**.*

<p>
<img src="huong-dan-su-dung/44-ky-xong-dien-thoai.png" alt="Đã ký xong" width="260">
<img src="huong-dan-su-dung/45-thoat-che-do-ky.png" alt="Nhân viên đăng nhập để thoát chế độ ký" width="260">
</p>

*Hình 3.3. Trái: **Đã ký xong 4 phiếu**, đưa máy lại cho nhân viên. Phải: nhân viên nhập mật khẩu, bấm **Đăng nhập và thoát chế độ ký**.*

**Ký một phiếu lẻ**

Giống như trên nhưng chỉ có một phiếu: phần **Nội dung phiếu (chỉ đọc)** ở trên, phần ký ở dưới. Phiếu có câu hỏi ý kiến thì chọn **Đồng ý** hoặc **Từ chối** (hoặc nhãn riêng của phiếu). Chạm **Xác nhận chữ ký**. Màn hình báo **Đã ghi nhận chữ ký**.

Khi người đại diện ký thay, dòng **Người ký** ghi rõ *"…, ký thay cho người bệnh …"*.

<p>
<img src="huong-dan-su-dung/46-ky-mot-phieu-dien-thoai.png" alt="Ký một phiếu: nội dung chỉ đọc" width="260">
<img src="huong-dan-su-dung/47-ky-ten-nguoi-dai-dien.png" alt="Người đại diện ký thay" width="260">
</p>

*Hình 3.4. Trái: phiếu lẻ, nội dung chỉ đọc ở trên. Phải: người đại diện ký thay cho người bệnh là trẻ em.*

### 3.2. Tự làm phiếu tại kiosk

Máy kiosk là màn hình cảm ứng 32 inch đặt dọc, có máy quét mã QR gắn bên dưới, camera, hoặc cả hai. Người bệnh làm 3 bước: **Nhận diện**, **Trả lời**, **Ký tên**. Thanh bước ở đầu màn hình cho biết đang ở bước nào.

**Bước 1: Nhận diện.** Trên màn hình đầu, chạm một trong ba cách:

- **Quét CCCD** (nhanh nhất): lật thẻ sang mặt có mã QR, đưa mã QR lại gần đèn đỏ của máy quét, giữ yên 1–2 giây. Nghe tiếng "bíp" là xong, máy tự chuyển sang bước sau. Máy chỉ đọc chữ trong mã QR, không chụp hay lưu ảnh thẻ.
  Kiosk có camera thì màn hình hiện khung hình camera: đưa mã QR vào khung vuông trên màn hình, cách camera khoảng một gang tay, giữ yên 1–2 giây. Đọc xong máy tự chuyển sang bước sau. Máy quét bên dưới (nếu có) vẫn dùng được cùng lúc. Hình từ camera chỉ dùng để đọc mã ngay trên máy, không lưu, không gửi đi.
  Mã QR căn cước trên ứng dụng VNeID cũng quét được như mã trên thẻ. Máy quét gõ sai chữ có dấu thì kiosk tự sửa; địa chỉ không sửa được hoặc bị mất chữ có dấu thì kiosk bỏ qua, không lưu chữ hỏng vào hồ sơ và phiếu. Họ tên bị mất chữ có dấu mà bệnh viện chưa có hồ sơ thì kiosk báo *"Máy chưa đọc đúng họ tên có dấu trên thẻ…"* và mời chọn **Nhập thông tin**; đã có hồ sơ thì kiosk dùng họ tên trong hồ sơ.
- **Nhập mã y tế hoặc số CCCD**: chọn **Mã y tế** hoặc **Số CCCD**, nhập bằng bàn phím số trên màn hình, chạm **Ô tiếp**, nhập **Ngày sinh (ngày/tháng/năm)**, chạm **Lấy thông tin**. Máy chỉ hiện thông tin khi ngày sinh khớp hồ sơ.
- **Nhập thông tin** (khi không mang CCCD hoặc chưa có mã y tế): nhập **Họ và tên**, **Ngày sinh**, **Giới tính**, **Số điện thoại**, **Số CCCD** (nếu có), **Chỗ ở hiện nay**, rồi chạm **Tiếp tục**. Bàn phím tiếng Việt có hàng phím dấu: gõ chữ rồi chạm dấu.

<p>
<img src="huong-dan-su-dung/50-kiosk-trang-dau.png" alt="Kiosk: màn hình đầu" width="250">
<img src="huong-dan-su-dung/51-kiosk-quet-cccd.png" alt="Kiosk: quét CCCD" width="250">
<img src="huong-dan-su-dung/51b-kiosk-quet-camera.png" alt="Kiosk có camera: quét CCCD bằng camera" width="250">
</p>

*Hình 3.5. Trái: màn hình đầu, chọn cách lấy thông tin. Giữa: kiosk chỉ có máy quét, hướng dẫn đưa mã QR trên thẻ vào máy quét. Phải: kiosk có camera, khung hình camera (lật như soi gương) với khung vuông chỉ chỗ đưa mã QR.*

<p>
<img src="huong-dan-su-dung/52-kiosk-nhap-ma.png" alt="Kiosk: nhập mã y tế và ngày sinh" width="250">
<img src="huong-dan-su-dung/53-kiosk-tu-khai.png" alt="Kiosk: tự nhập thông tin" width="250">
</p>

*Hình 3.6. Trái: nhập mã y tế và ngày sinh bằng bàn phím số. Phải: tự nhập thông tin; ô có dấu * là bắt buộc.*

Sau khi nhận diện, màn hình hỏi **Đây có phải thông tin của ông/bà?**. Chạm **Đúng, tiếp tục**. Nếu không đúng, chạm **Không phải tôi** để về màn hình đầu. Hồ sơ chưa có số điện thoại thì máy yêu cầu nhập số điện thoại.

Nếu ông/bà đã làm các phiếu này gần đây, máy hỏi **Thông tin của ông/bà có thay đổi so với lần trước không?**:

- **Không thay đổi, giữ như lần trước**: máy dùng lại câu trả lời lần trước (không hiện lại câu trả lời cũ), chỉ hỏi các câu còn lại;
- **Có thay đổi, trả lời lại**: trả lời lại từ đầu.

Câu Đồng ý/Không đồng ý và chữ ký luôn làm lại.

<p>
<img src="huong-dan-su-dung/54-kiosk-xac-nhan-thong-tin.png" alt="Kiosk: xác nhận thông tin" width="250">
<img src="huong-dan-su-dung/55-kiosk-lan-truoc.png" alt="Kiosk: hỏi thông tin có thay đổi so với lần trước" width="250">
</p>

*Hình 3.7. Trái: kiểm tra thông tin của mình, chạm **Đúng, tiếp tục**. Phải: câu hỏi giữ như lần trước hay trả lời lại.*

**Bước 2: Trả lời.** Mỗi màn hình một câu hỏi.

- Phía trên có danh sách **Phiếu của ông/bà hôm nay** (đã trả lời, đang làm, chưa làm) và thứ tự câu hỏi, ví dụ **Câu 2/6**.
- Câu có lựa chọn: chạm vào nút lớn. Câu cần gõ chữ: dùng bàn phím trên màn hình. Câu số: dùng bàn phím số.
- Câu hỏi ý kiến (Đồng ý/Không đồng ý): chạm **Xem nội dung phiếu** để đọc phiếu trước khi chọn.
- Chạm **Tiếp tục** để sang câu sau, **Quay lại** để về câu trước, **Làm lại từ đầu** (góc trên bên phải) để xóa hết và bắt đầu lại.
- Câu đã có trong hồ sơ thì máy không hỏi lại.

<p>
<img src="huong-dan-su-dung/56-kiosk-cau-hoi.png" alt="Kiosk: câu hỏi có lựa chọn" width="250">
<img src="huong-dan-su-dung/57-kiosk-ban-phim.png" alt="Kiosk: câu hỏi nhập chữ với bàn phím tiếng Việt" width="250">
<img src="huong-dan-su-dung/58-kiosk-dong-y.png" alt="Kiosk: câu hỏi Đồng ý hoặc Không đồng ý" width="250">
</p>

*Hình 3.8. Trái: câu có lựa chọn. Giữa: câu nhập chữ, có bàn phím tiếng Việt. Phải: câu hỏi Đồng ý/Không đồng ý, có nút **Xem nội dung phiếu**.*

**Bước 3: Ký tên.** Màn hình **Ký tên cho các phiếu**:

1. Xem lại danh sách phiếu và tóm tắt câu trả lời. Chạm **Xem nội dung** để đọc lại từng phiếu.
2. Chọn lời xác nhận (câu đầu tiên được chọn sẵn), hoặc chạm **Tự nhập nội dung khác** để tự gõ.
3. Ký vào khung.
4. Chạm **Ký xác nhận cho cả … phiếu** (một phiếu thì nút là **Ký xác nhận**).

Màn hình báo **Ông/bà đã ký xong … phiếu** và mời đến **quầy tiếp đón** để nhân viên kiểm tra và xác nhận. Màn hình hiện tên người bệnh, mã y tế (nếu có) và số phiếu. Sau 15 giây, máy tự về trang đầu và xóa thông tin khỏi màn hình. Muốn thay đổi nội dung, người bệnh báo nhân viên tại quầy.

<p>
<img src="huong-dan-su-dung/59-kiosk-ky-ten.png" alt="Kiosk: ký tên một lần cho các phiếu" width="250">
<img src="huong-dan-su-dung/60-kiosk-xong.png" alt="Kiosk: đã ký xong" width="250">
</p>

*Hình 3.9. Trái: chọn lời xác nhận, ký một lần cho cả bộ. Phải: đã ký xong, mời đến quầy tiếp đón.*

Lưu ý về kiosk:

- **60 giây không chạm** thì máy tự xóa thông tin và về trang đầu. 10 giây cuối có dòng nhắc *"Màn hình sẽ tự về trang đầu sau … giây"*. Chạm vào màn hình để tiếp tục.
- Màn hình quét ghi *"Camera của kiosk chưa dùng được (…)"*: người bệnh dùng máy quét bên dưới màn hình; nhân viên báo bộ phận CNTT (cần cho phép trình duyệt của kiosk dùng camera, xem hướng dẫn triển khai mục 9b).
- Nhập mã/CCCD kèm ngày sinh sai **5 lần** thì máy tạm khóa tra cứu **5 phút**. Trong lúc này, quét thẻ cũng phải chờ. Người bệnh vẫn chọn **Nhập thông tin** (không kèm số CCCD) được, hoặc đến quầy tiếp đón. Quản trị mở khóa sớm được: **Quản trị → Người dùng**, tài khoản kiosk có trạng thái **Tạm khóa tra cứu**, bấm **⋯** → **Mở khóa**.
- Các phiếu chuyên môn khác (nếu có) do điều dưỡng làm cùng người bệnh tại phòng khám.

**Dành cho nhân viên: đăng nhập và đăng xuất máy kiosk**

- Mỗi sáng, nhân viên bật máy và đăng nhập bằng **tài khoản kiosk** do quản trị cấp (lần đầu phải đổi mật khẩu). Phiên đăng nhập giữ trong ngày.
- Đăng xuất máy: **giữ biểu tượng bệnh viện ở góc trên bên trái 3 giây**, hộp **Đăng xuất máy kiosk?** hiện ra, bấm **Đăng xuất**.

<img src="huong-dan-su-dung/61-kiosk-dang-xuat.png" alt="Kiosk: hộp đăng xuất máy" width="250">

*Hình 3.10. Hộp đăng xuất máy kiosk, mở bằng cách giữ biểu tượng bệnh viện 3 giây.*

---

<a id="truong-khoa"></a>

## 4. Trưởng khoa

Trưởng khoa có mọi chức năng của mục 2, thêm **Thu hồi** phiếu đã ký, **Báo cáo** và **Xuất dữ liệu**. Số liệu và phiếu trong phạm vi khoa của trưởng khoa (và phiếu do trưởng khoa lập).

### 4.1. Thu hồi phiếu đã hoàn tất

Dùng khi phiếu đã ký có sai sót. Phiếu thu hồi hết hiệu lực nhưng vẫn được lưu nguyên, kể cả PDF đã ký.

1. Mở phiếu đã **Hoàn tất**.
2. Bấm **Thu hồi**.
3. Nhập lý do thu hồi (ít nhất 5 ký tự).
4. Bấm **Thu hồi** trong hộp.

<img src="huong-dan-su-dung/65-thu-hoi-phieu.png" alt="Hộp thu hồi phiếu" width="520">

*Hình 4.1. Thu hồi phiếu: bắt buộc ghi lý do.*

Phiếu chuyển sang **Đã thu hồi**, trang phiếu ghi **Lý do thu hồi** và thời điểm.

### 4.2. Lập phiếu thay thế

1. Mở phiếu **Đã thu hồi**.
2. Bấm **Lập phiếu thay thế**. Phần mềm tạo phiếu nháp mới, chép nội dung từ phiếu cũ, giữ khoa của phiếu cũ.
3. Sửa nội dung cho đúng, rồi trình ký và cho người bệnh ký như mục 2.6–2.10.

Mỗi phiếu thu hồi chỉ lập phiếu thay thế được một lần. Phiếu mới ghi **Thay thế cho phiếu …**, phiếu cũ ghi **Phiếu thay thế …** (bấm để mở). Người có quyền lập phiếu (tiếp đón, điều dưỡng, bác sĩ…) cũng bấm **Lập phiếu thay thế** được.

![Phiếu đã thu hồi](huong-dan-su-dung/66-phieu-da-thu-hoi.png)

*Hình 4.2. Phiếu đã thu hồi: trạng thái **Đã thu hồi**, dòng **Lý do thu hồi**, nút **Lập phiếu thay thế** ở góc phải.*

![Phiếu thay thế ở trạng thái nháp](huong-dan-su-dung/67-phieu-thay-the.png)

*Hình 4.3. Phiếu thay thế: dòng **Thay thế cho phiếu** dẫn về phiếu cũ, nội dung đã được chép sẵn.*

### 4.3. Báo cáo thống kê và xuất dữ liệu của khoa

Cách dùng như phần 6. Khác biệt: trưởng khoa chỉ thấy số liệu của khoa mình. Trang **Báo cáo thống kê** ghi *"Số liệu trong phạm vi khoa của bạn."* Trang **Xuất dữ liệu** ghi *"Bạn chỉ xuất được phiếu của khoa mình và phiếu do mình lập."* Số CCCD, số điện thoại, số thẻ BHYT trong file xuất được che một phần.

Trưởng khoa phụ trách nhiều khoa (tài khoản có **Khoa/phòng khác**, mục 8.1): số liệu gồm mọi khoa đó, trang ghi *"Số liệu trong phạm vi các khoa của bạn."* và có ô **Khoa/phòng (các khoa của bạn)** để xem hoặc xuất riêng từng khoa.

![Báo cáo thống kê của trưởng khoa](huong-dan-su-dung/68-bao-cao-truong-khoa.png)

*Hình 4.4. Báo cáo của trưởng khoa: không có ô chọn khoa, số liệu chỉ của khoa mình.*

---

<a id="quan-ly-mau-phieu"></a>

## 5. Quản lý mẫu phiếu

Người quản lý mẫu phiếu tạo và sửa mẫu, quản lý phiên bản và phát hành. Mỗi lần sửa là một **phiên bản mới**. Phiếu đã lập luôn giữ đúng phiên bản lúc lập.

### 5.1. Danh sách mẫu phiếu

Bấm **Mẫu phiếu** trên menu. Bảng có: mã và tên mẫu, **Xác nhận phía bệnh viện** (vai trò xác nhận cuối), **Phạm vi**, **Đang phát hành** (phiên bản nào, từ khi nào), **Nháp**, **Trạng thái** (**Đang dùng** hoặc **Ngừng sử dụng**). Bấm tên mẫu để mở.

![Danh sách mẫu phiếu](huong-dan-su-dung/70-danh-sach-mau.png)

*Hình 5.1. Danh sách mẫu phiếu. Nút **Thêm mẫu phiếu** ở góc phải.*

### 5.2. Xem mẫu

Trang chi tiết mẫu có:

- dòng thông tin: **Xác nhận phía bệnh viện**, **Phạm vi**, nút **Sửa thông tin mẫu**;
- bảng **Các phiên bản**: trạng thái (**Nháp**, **Đang phát hành**, **Ngừng dùng**), ngày cập nhật, ngày phát hành; nút **Xem**, **Phát hành**, **Ngừng dùng**;
- phần của phiên bản đang xem, có ba thẻ: **Xem trước** (thử điền để kiểm tra điều kiện hiển thị và cách tính điểm, dữ liệu thử không được lưu), **Cấu hình ký**, **Định nghĩa (JSON)**.

![Chi tiết mẫu phiếu](huong-dan-su-dung/71-chi-tiet-mau.png)

*Hình 5.2. Chi tiết mẫu phiếu: bảng **Các phiên bản** và thẻ **Xem trước** của phiên bản đang phát hành.*

### 5.3. Tạo phiên bản mới

1. Mở mẫu cần sửa.
2. Bấm **Tạo phiên bản mới**. Phần mềm chép phiên bản mới nhất thành phiên bản nháp. Nút này bị mờ khi mẫu đã có một phiên bản nháp.
3. Sửa phiên bản nháp ở thẻ **Soạn định nghĩa (JSON)** (mục 5.4) hoặc **Cấu hình ký** (mục 5.5).
4. Kiểm tra ở thẻ **Xem trước**.
5. Phát hành (mục 5.6).

Chỉ phiên bản nháp sửa được.

![Phiên bản nháp mới trong bảng các phiên bản](huong-dan-su-dung/72-phien-ban-nhap.png)

*Hình 5.3. Phiên bản 2 ở trạng thái **Nháp**, có nút **Phát hành**. Phiên bản 1 vẫn **Đang phát hành** cho đến khi phát hành phiên bản 2.*

### 5.4. Soạn định nghĩa (JSON)

Thẻ **Soạn định nghĩa (JSON)** chứa toàn bộ nội dung mẫu: tiêu đề, các phần, đoạn văn, câu hỏi, điều kiện hiển thị, cách tính điểm, tùy chọn in. Cấu trúc chi tiết xem tài liệu API ([docs/api.md](api.md), mục 6b) và [docs/mau-phieu.md](mau-phieu.md).

1. Mở thẻ **Soạn định nghĩa (JSON)** của phiên bản nháp.
2. Sửa nội dung trong ô soạn.
3. Bấm **Lưu phiên bản nháp**.
4. Nếu có lỗi, phần mềm liệt kê ở khung **Định nghĩa chưa hợp lệ** (hoặc báo *"Nội dung không phải JSON hợp lệ."*). Sửa rồi lưu lại.
5. Mở thẻ **Xem trước** để kiểm tra kết quả.

![Soạn định nghĩa JSON](huong-dan-su-dung/73-soan-json.png)

*Hình 5.4. Thẻ **Soạn định nghĩa (JSON)** của phiên bản nháp. Nút **Lưu phiên bản nháp** ở dưới ô soạn.*

Mẹo: ghi tiếng Anh trong ngoặc như các mẫu hiện có, ví dụ "Dân tộc (Ethnicity)". Phần mềm tự in phần tiếng Anh màu xanh, chữ nghiêng.

### 5.5. Cấu hình ký

Thẻ **Cấu hình ký** của phiên bản nháp cho phép cấu hình việc ký mà không cần sửa JSON:

- **Thứ tự ký**: các bước xác nhận trung gian, tối đa 5 bước. Mỗi bước chọn **Vai trò xác nhận** và nhập **Tên ô ký trên phiếu**, thêm **Ghi chú dưới tên ô ký** nếu cần. Bấm **Thêm bước xác nhận** để thêm. Dùng các nút mũi tên để đổi thứ tự, nút thùng rác để xóa. Bước xác nhận cuối do vai trò ở **Sửa thông tin mẫu** đảm nhận.
- **Ý kiến của người bệnh khi ký**: **Hỏi Đồng ý / Không đồng ý** (có thể đặt câu hỏi và nhãn riêng) hoặc **Chỉ ký xác nhận (không hỏi)**.
- **Lời xác nhận của người ký**: đánh dấu **Có ô lời xác nhận**, đặt tiêu đề ô, chọn **Bắt buộc ghi lời xác nhận trước khi ký**, thêm **Câu gợi ý (bấm chọn nhanh)**.
- **Tên các ô ký trên PDF**: tên ô người bệnh, người đại diện, ô xác nhận cuối. Ô để trống thì dùng nhãn mặc định.

Bấm **Lưu cấu hình ký**. Cấu hình chỉ áp dụng cho phiếu mới sau khi **phát hành** phiên bản. Phiếu đã lập vẫn ký theo phiên bản của nó.

![Cấu hình ký của phiên bản nháp](huong-dan-su-dung/74-cau-hinh-ky.png)

*Hình 5.5. Thẻ **Cấu hình ký** của một mẫu chỉ ký xác nhận, không có bước trung gian, có ô lời xác nhận bắt buộc.*

![Cấu hình ký có hai bước xác nhận](huong-dan-su-dung/75-cau-hinh-ky-nhieu-buoc.png)

*Hình 5.6. Ví dụ mẫu có hai bước xác nhận trung gian (Tiếp đón, rồi Điều dưỡng) trước bước xác nhận cuối của Bác sĩ.*

### 5.6. Phát hành, ngừng dùng

**Phát hành** phiên bản nháp:

1. Trong bảng **Các phiên bản**, bấm **Phát hành** ở dòng phiên bản nháp.
2. Bấm **Phát hành** trong hộp hỏi lại.

Từ lúc này, phiếu mới dùng phiên bản vừa phát hành. Phiên bản phát hành trước đó ngừng dùng cho phiếu mới. Phiếu đã lập giữ nguyên phiên bản cũ.

![Xác nhận phát hành phiên bản](huong-dan-su-dung/76-phat-hanh.png)

*Hình 5.7. Hộp hỏi lại trước khi phát hành phiên bản 2.*

**Ngừng dùng**: bấm **Ngừng dùng** ở phiên bản đang phát hành, rồi xác nhận. Mẫu không dùng được để lập phiếu mới cho đến khi phát hành phiên bản khác.

### 5.7. Sửa thông tin mẫu, thêm mẫu mới

**Sửa thông tin mẫu**: bấm **Sửa thông tin mẫu** để đổi **Tên mẫu**, **Vai trò xác nhận cuối (Xác nhận và hoàn tất)** và công tắc **Đang sử dụng** (tắt để không lập phiếu mới từ mẫu này). Bấm **Lưu**.

Đổi vai trò xác nhận cuối có hiệu lực **ngay**, cho cả phiếu đang chờ xác nhận của mẫu. Phần mềm hiện cảnh báo trước khi lưu.

<img src="huong-dan-su-dung/77-sua-thong-tin-mau.png" alt="Sửa thông tin mẫu" width="520">

*Hình 5.8. Đổi vai trò xác nhận cuối: cảnh báo *"Áp dụng ngay cho mọi phiếu chưa hoàn tất của mẫu này"*.*

**Thêm mẫu phiếu**: bấm **Thêm mẫu phiếu**, nhập **Mã mẫu** (in ở góc phiếu), **Tên mẫu**, chọn **Vai trò xác nhận phía bệnh viện**, bấm **Tạo**. Phần mềm tạo phiên bản nháp 1. Soạn nội dung (mục 5.4), cấu hình ký (mục 5.5) rồi phát hành.

---

<a id="bao-cao"></a>

## 6. Báo cáo / Xuất dữ liệu

Vai trò này xem báo cáo và xuất dữ liệu của toàn viện. Trưởng khoa dùng cùng các trang này nhưng chỉ trong phạm vi khoa mình (mục 4.3).

### 6.1. Báo cáo thống kê

1. Bấm **Báo cáo** trên menu.
2. Chọn khoảng ngày lập phiếu (mặc định từ đầu tháng đến hôm nay), **Mẫu phiếu**, **Khoa/phòng** nếu cần.

Báo cáo đếm phiếu theo **ngày lập** và theo **trạng thái hiện tại** của phiếu. Trang có:

- các ô **Tổng số phiếu**, **Hoàn tất**, **Chờ ký**, **Nháp**, **Đã hủy**, **Đã thu hồi**;
- **Ý kiến của người bệnh (phiếu hoàn tất)**: **Đồng ý**, **Từ chối**, **Tỉ lệ từ chối**;
- **Người ký phía người bệnh (phiếu hoàn tất)**: **Người bệnh tự ký**, **Người đại diện ký**;
- **Thời gian từ lập đến hoàn tất**: **Trung bình**, **Trung vị**, **90% phiếu dưới**, số phiếu theo từng mốc thời gian;
- biểu đồ **Số phiếu lập theo ngày**;
- bảng **Theo khoa/phòng** và **Theo mẫu phiếu**.

![Báo cáo thống kê toàn viện](huong-dan-su-dung/80-bao-cao.png)

*Hình 6.1. Báo cáo thống kê: bộ lọc ở trên, các ô số liệu, biểu đồ theo ngày và hai bảng theo khoa, theo mẫu.*

### 6.2. Xuất dữ liệu (Excel, CSV)

1. Bấm **Xuất dữ liệu** trên menu.
2. Ở **Bộ lọc**, chọn **Trạng thái**, **Mẫu phiếu**, **Khoa/phòng**, khoảng ngày lập. Để trống là lấy tất cả.
3. Ở **Định dạng và dữ liệu**, chọn **Excel (.xlsx)** hoặc **CSV (.csv)**.
4. Muốn có nội dung từng phiếu (mỗi câu hỏi một cột), đánh dấu **Kèm nội dung phiếu theo mẫu**. Ô này chỉ chọn được khi đã chọn đúng một mẫu ở bộ lọc.
5. Ở **Cột xuất**, đánh dấu các cột cần lấy. Bấm **Mặc định** để về bộ cột mặc định, **Chọn tất cả** để lấy mọi cột.
6. Bấm **Xuất file**. Trình duyệt tải file về. Phần mềm báo *"Đã xuất … phiếu: <tên file>"*.

![Trang xuất dữ liệu](huong-dan-su-dung/81-xuat-du-lieu.png)

*Hình 6.2. Xuất dữ liệu: bộ lọc và định dạng bên trái, chọn cột bên phải, nút **Xuất file** ở dưới.*

Lưu ý:

- Số CCCD, số điện thoại, số thẻ BHYT được che một phần (ví dụ `0790******12`). Cột có nhãn **che** là cột bị che. Chỉ người được cấp quyền riêng (mặc định là quản trị hệ thống) mới thấy ô **Xuất đầy đủ số CCCD, số điện thoại, số thẻ BHYT (dữ liệu nhạy cảm)**. Lần xuất đầy đủ được ghi nhật ký.
- Mỗi lần xuất tối đa 100.000 phiếu. Nhiều hơn thì thu hẹp bộ lọc.
- Xuất quá nhiều lần trong một phút (quá 10 lần), phần mềm báo *"Bạn thao tác quá nhiều lần"*. Chờ một phút rồi thử lại.
- File không lưu lại trên máy chủ. Hãy giữ file theo đúng quy định bảo mật của bệnh viện.

### 6.3. Lịch sử xuất

Phần **Lịch sử xuất của tôi** ở cuối trang **Xuất dữ liệu** liệt kê các lần bạn đã xuất: thời điểm, định dạng, bộ lọc, số phiếu, dữ liệu **Đã che** hay **Nhạy cảm đầy đủ**, **Kèm nội dung**, tên file, dung lượng, số cột, thời gian xuất và mã SHA-256 của file. Người có quyền xem nhật ký thấy **Lịch sử xuất (toàn hệ thống)**, có thêm cột **Người xuất**.

![Lịch sử xuất](huong-dan-su-dung/82-lich-su-xuat.png)

*Hình 6.3. **Lịch sử xuất của tôi**: một lần xuất Excel kèm nội dung và một lần xuất CSV, cả hai đều **Đã che**.*

---

<a id="kiem-tra"></a>

## 7. Kiểm tra

Vai trò Kiểm tra (thanh tra, pháp chế, quản lý chất lượng) chỉ đọc: xem phiếu và hồ sơ người bệnh của toàn viện, xem mẫu phiếu, báo cáo, danh sách người dùng và nhật ký hệ thống. Mỗi lần xem phiếu, xem hồ sơ người bệnh, tải PDF đều được ghi nhật ký.

### 7.1. Nhật ký hệ thống

1. Bấm **Nhật ký** trên menu.
2. Lọc theo **Hành động**, **Loại đối tượng**, **Mã đối tượng (Id)**, khoảng thời gian (**Từ thời điểm** – **Đến thời điểm**).
3. Bấm dấu **+** ở đầu dòng để xem chi tiết: **Mã yêu cầu** và nội dung thay đổi.
4. Với phiếu và người bệnh, bấm mã đối tượng để mở phiếu hoặc hồ sơ đó.

![Nhật ký hệ thống, một dòng đang mở chi tiết](huong-dan-su-dung/85-nhat-ky.png)

*Hình 7.1. Nhật ký hệ thống: mỗi dòng ghi thời điểm, người dùng, hành động, đối tượng, địa chỉ IP. Dòng đầu đang mở chi tiết.*

Nhật ký chỉ ghi thêm, không ai sửa hay xóa được. Giá trị nhạy cảm (số CCCD, số điện thoại, nội dung phiếu, mật khẩu) đã được che khi ghi.

### 7.2. Xem phiếu

Mở **Phiếu đồng thuận**, tìm và mở phiếu như mục 2.14. Người kiểm tra chỉ xem: không có nút sửa, xác nhận hay thu hồi. Bấm **Xem PDF đã ký** để xem bản đã ký. Phần **Lịch sử phiếu** cho biết ai làm gì, lúc nào, và mã SHA-256 của PDF đã ký.

![Người kiểm tra xem phiếu đã hoàn tất](huong-dan-su-dung/86-xem-phieu-kiem-tra.png)

*Hình 7.2. Người kiểm tra xem phiếu: chỉ có nút **Xem PDF đã ký**.*

---

<a id="quan-tri"></a>

## 8. Quản trị hệ thống

Quản trị hệ thống có toàn quyền: mọi chức năng ở các phần trên, cùng menu **Quản trị** gồm **Người dùng**, **Vai trò và quyền**, **Khoa/phòng**, **Cấu hình hệ thống**.

### 8.1. Người dùng

Bấm **Quản trị** → **Người dùng**. Danh sách chia hai thẻ: **Đang sử dụng** và **Ngưng sử dụng** (số trong ngoặc là số tài khoản của thẻ, quyết định 51). Tìm theo họ tên, tên đăng nhập, chức danh; lọc theo **Khoa/phòng**. Cột **Trạng thái** (thẻ Đang sử dụng): **Hoạt động**, **Tạm khóa** (nhập sai mật khẩu nhiều lần), **Tạm khóa tra cứu** (tài khoản kiosk).

![Danh sách người dùng](huong-dan-su-dung/90-nguoi-dung.png)

*Hình 8.1. Thẻ **Đang sử dụng**: khoa chính (dưới đó là dòng "Khoa khác: …" nếu có), vai trò, trạng thái, lần đăng nhập gần nhất. Cột **Thao tác** ở bên phải. Màn hình hẹp thì menu **Quản trị** nằm trong nút **···** trên thanh menu.*

Thêm người dùng:

1. Bấm **Thêm người dùng**.
2. Nhập **Tên đăng nhập** (chữ không dấu, số, dấu chấm, gạch ngang, gạch dưới), **Họ tên**, **Chức danh**.
3. Chọn **Khoa/phòng chính** (khoa mặc định khi lập phiếu, lập báo cáo điều dưỡng) và **Vai trò** (chọn được nhiều vai trò). Nhân viên làm ở nhiều khoa: chọn thêm ở ô **Khoa/phòng khác** (tối đa 20 khoa).
4. Nhập **Email**, **Số điện thoại** nếu có.
5. Nhập **Mật khẩu ban đầu** (theo quy tắc mật khẩu ở mục 1.2). Người dùng phải đổi mật khẩu ở lần đăng nhập đầu tiên.
6. Bấm **Lưu**. Báo tên đăng nhập và mật khẩu ban đầu cho người dùng theo cách an toàn.

<img src="huong-dan-su-dung/91-them-nguoi-dung.png" alt="Hộp thêm người dùng" width="620">

*Hình 8.2. Hộp **Thêm người dùng**: điều dưỡng khoa chính Khoa Khám bệnh, làm thêm ở Khoa Nội tổng hợp.*

Sửa, ngưng sử dụng, mở khóa, đặt lại mật khẩu (thẻ **Đang sử dụng**):

- Bấm **Sửa** để sửa họ tên, chức danh, khoa/phòng chính, khoa/phòng khác, vai trò, email, số điện thoại. Không đổi được tên đăng nhập.
- Bấm nút **⋯** ở cột **Thao tác** để chọn:
  - **Ngưng sử dụng**: khi nhân viên nghỉ việc, chuyển công tác, nghỉ dài ngày. Người dùng bị đăng xuất khỏi mọi thiết bị, không đăng nhập được; tài khoản chuyển sang thẻ **Ngưng sử dụng**. Phiếu, báo cáo, nhật ký đã có vẫn giữ nguyên;
  - **Mở khóa**: chỉ có khi tài khoản đang **Tạm khóa** (nhập sai mật khẩu) hoặc là tài khoản kiosk đang **Tạm khóa tra cứu** (người bệnh nhập sai ngày sinh nhiều lần). Mở khóa tài khoản kiosk thì máy tra cứu lại được ngay;
  - **Đặt lại mật khẩu**: đặt mật khẩu tạm. Mọi phiên đăng nhập của người dùng bị đăng xuất. Người dùng phải đổi mật khẩu ở lần đăng nhập tiếp theo.

<img src="huong-dan-su-dung/92-thao-tac-nguoi-dung.png" alt="Menu thao tác khác của một người dùng" width="600">

*Hình 8.3. Menu **⋯**: **Ngưng sử dụng**, **Đặt lại mật khẩu** (và **Mở khóa** khi tài khoản đang tạm khóa).*

Thẻ **Ngưng sử dụng** (quyết định 51): các tài khoản đã ngưng sử dụng, mỗi dòng có **Sửa**, **Sử dụng lại**, **Xóa**.

- **Sử dụng lại**: khi nhân viên quay lại làm việc. Người dùng đăng nhập lại được ngay với mật khẩu hiện tại (quên mật khẩu thì **Đặt lại mật khẩu** ở thẻ **Đang sử dụng**). Nên kiểm tra lại khoa, vai trò bằng nút **Sửa** trước khi sử dụng lại.
- **Xóa**: khi tài khoản tạo nhầm, trùng, hoặc không còn cần giữ trong danh sách. Chỉ xóa được tài khoản đã ngưng sử dụng (ngưng sử dụng trước, rồi xóa ở thẻ này). Bấm **Xóa**, đọc hộp xác nhận rồi bấm **Xóa**:
  - tài khoản biến khỏi cả hai thẻ, không đăng nhập và không sử dụng lại được; vai trò, khoa của tài khoản được gỡ;
  - **tên đăng nhập đó không tạo lại được** (để nhật ký không nhầm người); cần tài khoản mới thì đặt tên đăng nhập khác;
  - phiếu đã ký, báo cáo, nhật ký vẫn giữ tên người này; nhật ký hệ thống ghi người xóa, lúc xóa;
  - **không hoàn tác được**.

![Thẻ Ngưng sử dụng: các tài khoản đã ngưng, nút Sửa, Sử dụng lại, Xóa](huong-dan-su-dung/92a-ngung-su-dung.png)

*Hình 8.3a. Thẻ **Ngưng sử dụng**: tài khoản nghỉ việc, chuyển công tác và một tài khoản tạo nhầm (dữ liệu giả lập).*

![Hộp xác nhận xóa tài khoản](huong-dan-su-dung/92b-xoa-tai-khoan.png)

*Hình 8.3b. Hộp xác nhận **Xóa tài khoản**: tài khoản tạo nhầm được xóa; tên đăng nhập không tạo lại được.*

Dữ liệu theo khoa:

- Tài khoản thấy phiếu, trang Tổng quan, báo cáo thống kê, xuất dữ liệu và báo cáo điều dưỡng của **khoa chính và các khoa khác** của mình, cùng phiếu do mình lập. Danh sách người bệnh cũng theo khoa (mục 2.1): người bệnh có phiếu ở các khoa đó, phiếu mình lập hoặc hồ sơ mình tiếp nhận; người bệnh khác tìm bằng đúng mã người bệnh hoặc đủ 12 số CCCD.
- Danh sách người dùng ghi khoa chính, dưới đó là *Khoa khác: …*. Lọc theo **Khoa/phòng** tìm cả người có khoa đó là khoa khác.
- Người cần xem mọi khoa (Kế hoạch tổng hợp, ban giám đốc, kiểm tra): không cần chọn hết các khoa, hãy gán vai trò có quyền **Xem dữ liệu toàn viện** (mục 8.3).
- Đổi khoa của tài khoản có hiệu lực sau tối đa 15 phút, hoặc ngay khi người dùng đăng xuất rồi đăng nhập lại.
- Khoa đang gán cho tài khoản nào (khoa chính hay khoa khác) thì không xóa được, chỉ chuyển sang ngừng sử dụng (mục 8.4).

### 8.2. Tài khoản cho máy kiosk

Mỗi máy kiosk dùng một tài khoản riêng:

1. **Thêm người dùng**, ví dụ tên đăng nhập `kiosk.kkb`.
2. Chọn **Khoa/phòng chính** là khoa đặt máy. Phiếu lập trên kiosk thuộc khoa này. Tài khoản kiosk không có khoa/phòng khác.
3. Chọn **chỉ một** vai trò **Kiosk người bệnh**. Phần mềm từ chối nếu thêm vai trò khác hoặc không chọn khoa.
4. Đặt mật khẩu ban đầu, bấm **Lưu**. Nhân viên đăng nhập máy kiosk lần đầu và đổi mật khẩu.
5. Chọn các mẫu dùng trên kiosk ở **Cấu hình hệ thống** (mục 8.5).

Tài khoản kiosk chỉ dùng được màn hình kiosk. Mở trang khác cũng bị đưa về kiosk.

<img src="huong-dan-su-dung/93-tao-tai-khoan-kiosk.png" alt="Tạo tài khoản kiosk" width="620">

*Hình 8.4. Tài khoản kiosk: một vai trò **Kiosk người bệnh** và khoa đặt máy.*

### 8.3. Vai trò và quyền

Bấm **Quản trị** → **Vai trò và quyền**. Cột trái liệt kê các vai trò, mỗi vai trò ghi mã và số quyền. Vai trò có sẵn của phần mềm mang nhãn **Có sẵn**; vai trò quản trị thêm mang nhãn **Tự tạo**.

**Sửa quyền của vai trò:**

1. Chọn vai trò ở cột trái.
2. Đánh dấu hoặc bỏ dấu các quyền theo nhóm: **Hệ thống**, **Người bệnh**, **Mẫu phiếu**, **Phiếu đồng thuận**, **Báo cáo và xuất dữ liệu**, **Báo cáo điều dưỡng**, **Phạm vi dữ liệu**, **Kiểm thử**, **Nội kiểm đường huyết** (mục 11).
3. Bấm **Lưu thay đổi** (hoặc **Hủy thay đổi** để bỏ).

**Thêm vai trò** (ví dụ vai trò cho Ban giám đốc, Dược sĩ lâm sàng):

1. Bấm **Thêm vai trò** ở góc phải.
2. Nhập **Tên vai trò** (không trùng tên vai trò khác) và **Mô tả** nếu cần.
3. Muốn bắt đầu từ quyền của một vai trò có sẵn thì chọn vai trò đó ở ô **Lấy sẵn quyền từ vai trò**. Không chọn thì vai trò mới chưa có quyền nào.
4. Bấm **Lưu**. Vai trò mới được chọn ngay ở cột trái. Đánh dấu, bỏ dấu quyền cho đúng việc rồi bấm **Lưu thay đổi**.
5. Gán vai trò cho nhân viên ở **Quản trị → Người dùng** (mục 8.1).

Mã vai trò được tạo tự động từ tên, bắt đầu bằng `VT_` (ví dụ `VT_DUOC_SI_LAM_SANG`), và không đổi về sau. Vai trò mới chọn được ở cấu hình ký của mẫu phiếu, như vai trò có sẵn.

**Sửa tên, mô tả:** chọn vai trò, bấm **Sửa tên, mô tả**, sửa rồi bấm **Lưu**. Đổi được cả tên vai trò có sẵn. Mã vai trò giữ nguyên, nên tài khoản và mẫu phiếu đang dùng vai trò không bị ảnh hưởng.

**Xóa vai trò:** chỉ xóa được vai trò **Tự tạo**. Chọn vai trò, bấm **Xóa vai trò** rồi **Xóa**. Phần mềm không cho xóa khi:

- vai trò còn gán cho tài khoản nào, kể cả tài khoản đã khóa: bỏ vai trò khỏi các tài khoản đó ở **Quản trị → Người dùng** trước;
- vai trò đang dùng trong cấu hình ký của mẫu phiếu (bước xác nhận cuối hoặc bước xác nhận trung gian, kể cả phiên bản cũ): chọn vai trò khác cho mẫu đó trước.

Người dùng nhận quyền mới khi phiên đăng nhập được làm mới (tối đa 15 phút). Vai trò **Quản trị hệ thống** luôn có toàn bộ quyền. Vai trò **Kiosk người bệnh** có quyền cố định. Quyền của hai vai trò này không sửa được, và quyền **Dùng kiosk người bệnh tự làm phiếu** chỉ dành cho vai trò Kiosk người bệnh.

Quyền **Xem dữ liệu toàn viện** cho phép xem phiếu của mọi khoa. Không có quyền này thì chỉ thấy phiếu của các khoa của mình (khoa chính và khoa khác, mục 8.1) và phiếu mình lập.

<img src="huong-dan-su-dung/94a-them-vai-tro.png" alt="Hộp Thêm vai trò" width="420">

*Hình 8.5a. Hộp **Thêm vai trò**: tên, mô tả, lấy sẵn quyền từ vai trò **Điều dưỡng**.*

![Vai trò và quyền](huong-dan-su-dung/94-vai-tro-quyen.png)

*Hình 8.5. Vai trò **Dược sĩ lâm sàng** vừa thêm (nhãn **Tự tạo**, mã `VT_DUOC_SI_LAM_SANG`), quyền lấy sẵn từ vai trò Điều dưỡng; có nút **Sửa tên, mô tả** và **Xóa vai trò**.*

### 8.4. Khoa/phòng

Bấm **Quản trị** → **Khoa/phòng**.

**Khi phần mềm đã kết nối API thông tin người bệnh**, danh mục khoa/phòng **lấy từ HIS**. Trang có dòng *"Danh mục khoa/phòng lấy từ HIS: không thêm, sửa, xóa ở đây."* và không có nút thêm, sửa, xóa. Khi HIS thêm, đổi tên hoặc ngừng dùng khoa/phòng, bấm **Lấy danh mục từ HIS**:

- Khoa/phòng trùng mã trên HIS: cập nhật tên theo HIS. Khoa/phòng nhập tay trước đây có tên trùng tên trên HIS: nhận mã HIS, người dùng và phiếu đã gắn giữ nguyên. Khoa/phòng chỉ có trên HIS: thêm mới.
- Khoa/phòng không còn trên HIS: chuyển sang **Ngừng sử dụng**. Màn hình ghi tên các khoa này; chuyển người dùng của khoa đó sang khoa mới ở **Người dùng** (mục 8.1).
- Màn hình báo lỗi (API chưa có câu truy vấn danh mục khoa/phòng, HIS không trả lời) thì danh mục giữ nguyên. Báo phòng CNTT (`docs/trien-khai.md` mục 5.9).

**Khi chưa kết nối API thông tin người bệnh**, quản trị tự quản lý danh mục:

- **Thêm khoa/phòng**: nhập **Mã khoa** (ví dụ KKB), **Tên khoa/phòng**, chọn **Trạng thái** (**Đang sử dụng** hoặc **Ngừng sử dụng**), bấm **Lưu**.
- **Sửa**: đổi mã, tên, trạng thái.
- **Xóa**: chỉ xóa được khoa chưa có người dùng (không là khoa chính hay khoa khác của tài khoản nào). Khoa không dùng nữa thì đặt **Ngừng sử dụng**.

![Danh sách khoa/phòng](huong-dan-su-dung/95-khoa-phong.png)

*Hình 8.6. Danh sách khoa/phòng khi chưa kết nối API thông tin người bệnh (có nút thêm, sửa, xóa).*

### 8.5. Cấu hình hệ thống

Bấm **Quản trị** → **Cấu hình hệ thống**. Mọi thay đổi ở đây được ghi nhật ký hệ thống.

**Tên đơn vị in trên phiếu.** Nhập **Cơ quan chủ quản**, **Tên bệnh viện**, **Địa danh** (in ở dòng ngày tháng, ví dụ "Hà Nội, ngày … tháng … năm …"), bấm **Lưu**. Phiếu in sau khi lưu dùng tên mới. PDF đã ký giữ nguyên. Khi chưa nhập, phiếu dùng giá trị mẫu như trong ảnh. Hãy nhập tên đơn vị thật trước khi đưa vào sử dụng.

**Logo bệnh viện.** Bấm **Tải logo lên** (hoặc **Thay logo**), chọn ảnh PNG hoặc JPG, tối đa 1 MB, nên là ảnh vuông nền trắng hoặc trong suốt, rộng từ 300 điểm ảnh. Logo in ở góc trên bên trái mọi phiếu tạo sau khi lưu. Bấm **Bỏ logo** để bỏ.

![Cấu hình tên đơn vị và logo](huong-dan-su-dung/96-cau-hinh-ten-don-vi-logo.png)

*Hình 8.7. **Tên đơn vị in trên phiếu** (đang là giá trị mẫu) và **Logo bệnh viện** (chưa có logo).*

**Khung chữ ký.** Ảnh thiết kế của bệnh viện (logo, viền màu) làm nền khung ký trên điện thoại, kiosk, máy tính và khung chữ ký mẫu. Trên PDF, mỗi chỗ ký in khung này: đã ký thì chữ ký nằm giữa khung, chưa ký thì khung trống. Ảnh phải là **PNG**, tối đa 1 MB, nên ngang tỉ lệ 4:3, rộng từ 1200 điểm ảnh. Bấm **Tải khung lên** (hoặc **Thay khung**, **Bỏ khung**). Có hiệu lực ở màn hình ký mở sau khi lưu và PDF tạo sau khi lưu.

**Kiosk người bệnh tự làm phiếu.** Chọn các mẫu người bệnh tự làm trên kiosk, **theo thứ tự làm**, rồi bấm **Lưu mẫu kiosk**. Chỉ chọn được mẫu đã phát hành. Chỉ dùng mẫu người bệnh tự trả lời được (không có bảng nhiều dòng hay ngày giờ). Phiếu nhiều mục chuyên môn (ví dụ BM10) để điều dưỡng làm. Mẫu nào không dùng được trên kiosk thì có khung cảnh báo.

![Cấu hình khung chữ ký và kiosk](huong-dan-su-dung/97-cau-hinh-khung-chu-ky-kiosk.png)

*Hình 8.8. **Khung chữ ký** (chưa có khung) và **Kiosk người bệnh tự làm phiếu** với ba mẫu theo thứ tự làm.*

**Gắn phiếu với lượt tiếp nhận (HIS).** Bật công tắc **Bắt buộc chọn lượt tiếp nhận khi ký hoàn tất phiếu**: người ký hoàn tất phải chọn lượt tiếp nhận (mục 2.11). Có hiệu lực ngay, không cần khởi động lại.

- Công tắc chỉ bật được khi phòng CNTT đã cấu hình kết nối hệ thống thông tin người bệnh. Chưa cấu hình thì công tắc bị mờ và có khung thông báo.
- **HIS lỗi thì tắt** để nhân viên hoàn tất phiếu tiếp (phiếu hoàn tất lúc tắt không gắn lượt tiếp nhận). HIS ổn định thì bật lại.

**Kế thừa phiếu lần trước.** Nhập số ngày (0–365, **0 là tắt**), bấm **Lưu**. Cách hoạt động: xem mục 2.7 và 3.2.

![Cấu hình lượt tiếp nhận và kế thừa phiếu](huong-dan-su-dung/98-cau-hinh-luot-tiep-nhan-ke-thua.png)

*Hình 8.9. Công tắc gắn lượt tiếp nhận đang bật; kế thừa phiếu lần trước 30 ngày.*

**Cấu hình triển khai (chỉ đọc).** Thông tin do phòng CNTT đặt trên máy chủ. Không sửa được ở đây.

- **API thông tin người bệnh**: **Đã kết nối** thì có nút **Lấy thông tin**, chọn lượt tiếp nhận, lấy số liệu HIS cho báo cáo điều dưỡng; **Chưa cấu hình** thì các chức năng này ẩn hoặc nhập tay.
- **Tra cứu HIS tự động khi quét CCCD**: cách kết nối cũ, đang tạm hoãn. Ghi **Không dùng** là bình thường, kể cả khi API thông tin người bệnh đã kết nối.
- **Ký số VNPT SmartCA**, **Giới hạn mỗi lần xuất dữ liệu**, **Lưu trữ**.

<img src="huong-dan-su-dung/98-cau-hinh-trien-khai.png" alt="Cấu hình triển khai (chỉ đọc)" width="440">

*Hình 8.10. Cấu hình triển khai trên máy thử (chưa cấu hình API thông tin người bệnh).*

### 8.6. Chữ ký mẫu

Mỗi nhân viên tự ký chữ ký mẫu của mình ở **Tài khoản của tôi** (mục 1.5). Quản trị không ký thay được. Khi xác nhận phiếu, tài khoản đã có chữ ký mẫu thì ký bằng chữ ký mẫu, không phải vẽ lại. Vẫn chọn **Ký tay lần này** được. Tài khoản chưa có chữ ký mẫu thì vẽ vào khung ký mỗi lần xác nhận. Hãy nhắc nhân viên xác nhận phiếu (tiếp đón, điều dưỡng, bác sĩ) ký chữ ký mẫu trước khi dùng.

<a id="bao-tri"></a>

### 8.7. Bảo trì hệ thống

Dùng khi cần điều chỉnh cấu hình, mẫu phiếu, dữ liệu mà không để người khác thao tác cùng lúc. Trong lúc bảo trì, người dùng khác **không đăng nhập, không thao tác được**, kể cả kiosk và người bệnh đang ký qua mã QR. Tài khoản quản trị vẫn làm việc như thường.

**Bật chế độ bảo trì.** Bấm **Quản trị** → **Bảo trì hệ thống**. Nhập **Lý do** (hiện cho người dùng, ví dụ "Nâng cấp phần mềm, cập nhật mẫu phiếu") và **Dự kiến xong lúc** (để trống nếu chưa rõ), bấm **Bật chế độ bảo trì**, đọc thông báo rồi bấm **Bật bảo trì**. Có hiệu lực ngay.

![Trang Bảo trì hệ thống](huong-dan-su-dung/bt-01-trang-bao-tri.png)

*Hình 8.11. Đang bảo trì: ghi lúc bật, người bật; sửa được lý do, giờ dự kiến xong.*

**Người dùng thấy gì.** Mọi máy đang mở phần mềm (máy tính, máy tính bảng, kiosk, điện thoại người bệnh) hiện màn hình **"Hệ thống đang bảo trì"** kèm lý do và giờ dự kiến xong, ngay khi họ thao tác (mở trang, bấm lưu…). Người chưa đăng nhập thấy màn hình này thay cho trang đăng nhập.

- Trang họ đang mở và **dữ liệu đang nhập vẫn giữ** dưới màn hình bảo trì. Màn hình tự kiểm tra lại mỗi 30 giây (hoặc bấm **Kiểm tra lại**). Bảo trì xong, màn hình tự đóng, làm tiếp được, không phải đăng nhập lại. Thao tác bấm lúc đang bảo trì chưa được lưu: bấm lưu lại.
- Kiosk và điện thoại người bệnh có lời mời chờ hoặc liên hệ nhân viên, không có nút của nhân viên.

![Màn hình Hệ thống đang bảo trì](huong-dan-su-dung/bt-02-man-hinh-bao-tri.png)

*Hình 8.12. Màn hình bảo trì trên máy của một điều dưỡng: trang đang mở (ô tìm người bệnh vừa gõ) vẫn giữ bên dưới.*

**Quản trị trong lúc bảo trì.** Tài khoản có quyền **Bảo trì hệ thống** (vai trò Quản trị hệ thống; thêm cho vai trò khác ở mục 8.3) vẫn đăng nhập, thao tác như thường, và thấy dải vàng **"Đang bật chế độ bảo trì"** ở đầu mọi trang. Máy đang hiện màn hình bảo trì mà quản trị cần đăng nhập: bấm **Đăng nhập bằng tài khoản quản trị** (máy đang giữ phiên của tài khoản khác thì bấm **Đăng xuất** trước). Tài khoản khác đăng nhập ở đây bị báo *Trong lúc bảo trì chỉ tài khoản có quyền bảo trì hệ thống đăng nhập được*.

![Dải báo đang bảo trì cho quản trị](huong-dan-su-dung/bt-03-dai-bao-quan-tri.png)

*Hình 8.13. Quản trị vẫn làm việc khi đang bảo trì, có dải vàng nhắc.*

**Tắt chế độ bảo trì.** Vào lại **Bảo trì hệ thống**, bấm **Tắt chế độ bảo trì**. Mọi người làm việc lại được ngay. Giờ dự kiến xong chỉ để thông báo: **hết giờ hệ thống không tự tắt**. Bảo trì lâu hơn dự kiến thì sửa giờ, bấm **Lưu lý do, giờ dự kiến** để người dùng thấy giờ mới.

Mọi lần bật, sửa, tắt đều ghi **Nhật ký hệ thống**. Không còn tài khoản quản trị nào đăng nhập được thì phòng CNTT tắt bảo trì theo hướng dẫn triển khai (mục 5.12). Khi phòng CNTT cài bản mới hoặc chạy script xóa dữ liệu chạy thử, người dùng cũng thấy màn hình bảo trì vài phút; xong thì trang tự tải lại.

---

<a id="bao-cao-dieu-duong"></a>

## 9. Báo cáo điều dưỡng hằng ngày

Mỗi khoa lập một **Báo cáo công tác điều dưỡng** mỗi ngày theo mẫu giấy đang dùng, rồi gửi Phòng Điều dưỡng. Phòng Điều dưỡng xem **bảng tổng hợp** của các khoa theo mẫu giấy "Tổng kết báo cáo Phòng Điều dưỡng hằng ngày", in PDF và xuất Excel.

| Ai | Làm gì | Menu |
|---|---|---|
| Điều dưỡng của khoa | Lập, lưu nháp, gửi báo cáo của khoa mình (mục 9.1). Khi HIS lỗi: nhập số liệu HIS của khoa (mục 9.5). Khi vắng: ủy quyền cho người khác làm thay; được ủy quyền thì làm báo cáo của khoa khác trong thời gian được ủy quyền (mục 9.7) | **Điều dưỡng** → **Báo cáo của khoa**, **Ủy quyền báo cáo** |
| Khoa được giao nhập số liệu HIS (ví dụ Khoa Khám bệnh, Khoa Chẩn đoán hình ảnh) | Khi HIS lỗi: nhập các chỉ số mục B, D được giao (mục 9.5) | **Điều dưỡng** → **Báo cáo của khoa** |
| Phòng Điều dưỡng | Xem bảng tổng hợp; xem, in và trả lại báo cáo cho khoa; cài khoa báo cáo, số giường, khoa nhập số liệu HIS; lấy số liệu HIS (mục 9.2–9.5); xem biểu đồ nhiều ngày (mục 9.6); xem và hủy ủy quyền của các khoa (mục 9.7). Mọi việc trên bảng tổng hợp và biểu đồ làm ở một trang **Dashboard**. Không nhập số liệu thay khoa | **Điều dưỡng** → **Dashboard**, **Ủy quyền báo cáo** |

### 9.1. Lập và gửi báo cáo của khoa (điều dưỡng)

1. Bấm **Điều dưỡng** trên menu, chọn **Báo cáo của khoa**.

   ![Menu Điều dưỡng của tài khoản điều dưỡng](huong-dan-su-dung/dd-00-menu.png)

   *Hình 9.1. Menu **Điều dưỡng** chỉ hiện với tài khoản được lập hoặc xem báo cáo điều dưỡng.*

2. Báo cáo lập cho khoa của tài khoản (ghi dưới tên phần mềm, góc trên bên trái). Tài khoản làm ở nhiều khoa có thêm ô chọn khoa cạnh ô ngày: mặc định khoa chính, chọn khoa khác để lập báo cáo của khoa đó. Ngày báo cáo mặc định là hôm nay. Muốn lập bù cho ngày trước, chọn ngày ở góc trên bên phải. Lập bù được trong vòng một năm; không chọn được ngày sau hôm nay.
3. Nhập số liệu theo mẫu giấy. Ô nào không có số thì để trống. Mục **I. Nhân sự** và ô **Số NB theo chỉ tiêu** được điền sẵn theo báo cáo khoa đã gửi gần nhất: kiểm tra lại cho đúng ngày hôm nay. Ở mục **VI. Số lượng NB**:
   - **Số NB ngày hôm trước** được điền sẵn bằng **Số NB hiện tại** của báo cáo khoa đã gửi hôm trước. Hôm trước khoa chưa gửi báo cáo, hoặc số chưa đúng, thì nhập tay.
   - **Số NB hiện tại** không nhập: phần mềm tự tính = Số NB ngày hôm trước + Số ca nhập khoa từ PK + Số ca nhập khoa do CK − Số ca xuất viện − Số ca chuyển khoa (các số ca đều của ngày hôm trước). Số tự đổi khi nhập các ô này. Số âm có viền đỏ: có ô nhập nhầm. Gửi báo cáo thì phải có Số NB ngày hôm trước.
   - Số ca nhập viện ngày hôm trước nhập riêng hai dòng: **Số ca nhập khoa từ PK ngày hôm trước** (từ phòng khám) và **Số ca nhập khoa do CK ngày hôm trước** (khoa khác chuyển đến).
   - **Số NB người nước ngoài**: trong số NB hiện tại, số người bệnh là người nước ngoài. **Số ca chuyển khoa ngày hôm trước**: số người bệnh khoa chuyển sang khoa khác.
   - **Số NB bao phòng** (ngay sau Số NB người nước ngoài): trong số NB hiện tại, số người bệnh bao phòng. Khoa nhập mỗi ngày, không điền sẵn theo hôm trước.

   ![Báo cáo mới của khoa, có số điền sẵn](huong-dan-su-dung/dd-01-bao-cao-khoa.png)

   ![Mục VI: số NB ngày hôm trước điền sẵn, số NB hiện tại tự tính, dòng Số NB bao phòng](huong-dan-su-dung/dd-01b-so-luong-nb.png)

   *Hình 9.2. Báo cáo chưa lập (nhãn **Chưa lập**). Khung xanh cho biết các số đã được điền sẵn, gồm số NB ngày hôm trước. Ở mục VI, **Số NB hiện tại** tự tính theo công thức ghi dưới tên dòng, không nhập được; dòng **Số NB bao phòng** nằm ngay sau Số NB người nước ngoài.*

4. Ở **Phần tick**, mỗi dòng chọn **Có sự cố/ Không ổn** hoặc **Không có sự cố/ Ổn**. Phải chọn đủ mọi dòng mới gửi được.
5. Ở **Phần báo cáo (nhập thêm)**, ghi các vấn đề bất thường trong ngày (nếu có). Xuống dòng được. Tối đa 20.000 ký tự, kể cả dấu cách và xuống dòng; bộ đếm dưới ô cho biết đã gõ bao nhiêu ký tự.

   ![Phần tick và phần báo cáo nhập thêm](huong-dan-su-dung/dd-02-phan-tick.png)

   *Hình 9.3. Mỗi dòng của phần tick chọn một trong hai ô. Nút **Lưu nháp** và **Gửi báo cáo** ở cuối trang.*

6. Chưa xong thì bấm **Lưu nháp**. Báo cáo nháp chưa được tính vào bảng tổng hợp.
7. Xong thì bấm **Gửi báo cáo**, rồi bấm **Gửi** để xác nhận. Bấm **Xem lại** nếu muốn sửa tiếp.

   ![Hộp xác nhận gửi báo cáo](huong-dan-su-dung/dd-03-xac-nhan-gui.png)

   *Hình 9.4. Xác nhận gửi. Sau khi gửi, khoa không sửa được nữa.*

8. Gửi xong, báo cáo bị khóa và có nhãn **Đã gửi** kèm giờ gửi, người gửi. Bấm **In PDF** để xem, in hoặc tải báo cáo.

   ![Báo cáo đã gửi](huong-dan-su-dung/dd-04-da-gui.png)

   *Hình 9.5. Báo cáo đã gửi: các ô không sửa được, có nút **In PDF**.*

   <img src="huong-dan-su-dung/dd-11-pdf-bao-cao-khoa.png" alt="PDF báo cáo của khoa, trang 1" width="560">

   *Hình 9.6. PDF báo cáo của khoa (trang 1) theo bố cục mẫu giấy. Chân trang ghi giờ gửi và người gửi.*

Lưu ý:

- Cần sửa báo cáo đã gửi: đề nghị Phòng Điều dưỡng **trả lại khoa** (mục 9.3). Khi được trả lại, trang báo cáo hiện khung ghi ai trả lại và lý do. Sửa rồi bấm **Gửi báo cáo** lại.
- Dưới báo cáo có thẻ **Số liệu HIS khoa nhập khi HIS lỗi**. Chỉ nhập khi HIS lỗi (mục 9.5).
- Trang báo **Khoa chưa có trong danh sách khoa báo cáo điều dưỡng**: liên hệ Phòng Điều dưỡng để được thêm vào danh sách (mục 9.4). Trong lúc chờ, bạn chỉ xem được, không lưu được.
- Tài khoản ghi sai khoa: nhờ quản trị sửa khoa/phòng của tài khoản (mục 8.1).
- Phần nhập thêm có thể ghi tên người bệnh. Phần này được mã hóa khi lưu, và nhật ký hệ thống không ghi nội dung của nó. Mỗi lần lưu, gửi, trả lại khoa đều được ghi nhật ký.

### 9.2. Bảng tổng hợp hằng ngày (Phòng Điều dưỡng)

Bảng tổng hợp nằm trong trang **Dashboard** điều dưỡng (không còn mục menu **Tổng hợp hằng ngày** riêng; đường dẫn cũ đã lưu trên trình duyệt tự mở Dashboard).

1. Bấm **Điều dưỡng** trên menu, chọn **Dashboard**. Chọn ngày ở ô **Ngày báo cáo**, góc trên bên phải (mặc định hôm nay). Một ô ngày dùng cho cả trang: ô chỉ số, bảng tổng hợp, PDF, Excel và biểu đồ đều theo ngày này.
2. Phần trên cùng là **tám ô chỉ số** của ngày đó (mục 9.6), trong đó ô **Khoa đã gửi báo cáo** ghi số khoa đã gửi trên số khoa phải báo cáo, kèm tên viết tắt các khoa chưa gửi để nhắc. Dưới các ô chỉ số có hai thẻ: **Bảng tổng hợp** (mở sẵn) và **Biểu đồ** (mục 9.6).
3. Ở thẻ **Bảng tổng hợp**, ô đầu tiên ghi:
   - Số liệu HIS lấy lúc nào, bởi ai.
   - Khi HIS lỗi: dòng **Khoa nhập tay khi HIS lỗi** cho biết khoa nào đã nhập số liệu HIS, khoa nào chưa nhập (mục 9.5).
   - Các nút **In PDF**, **Xuất Excel** (bước 9), và với tài khoản Phòng Điều dưỡng: **Lấy số liệu HIS** (mục 9.5), **Khoa báo cáo và số giường** (mục 9.4).
4. **Mục A** có một dòng cho mỗi khoa phải báo cáo, ghi bằng **tên viết tắt** của khoa (khoa chưa đặt tên viết tắt thì ghi mã khoa), theo **thứ tự Phòng Điều dưỡng xếp**. Phòng Điều dưỡng đặt thứ tự và tên viết tắt ở mục 9.4. Rê chuột vào tên viết tắt để xem tên khoa. Mỗi dòng kèm trạng thái **Đã gửi**, **Nháp** hoặc **Chưa lập**. Chỉ báo cáo **đã gửi** được tính. Khoa chưa gửi thì để trống các ô do khoa báo cáo, chỉ có số giường và cột **NB PK chờ nhập viện**. Dòng **Tổng** cộng các khoa.
   - Số ca nhập viện ngày hôm trước chia hai cột **Nhập khoa từ PK** và **Nhập khoa do CK**; báo cáo đã gửi trước khi tách cột chỉ có số tổng nên hai cột này để trống.
   - Cột **Người nước ngoài** và cột **Chuyển khoa** lấy từ hai dòng cùng tên ở mục VI của báo cáo khoa. Báo cáo gửi trước khi có hai dòng này thì hai cột để trống.
   - Cột **Bao phòng** (ngay sau cột Người nước ngoài) lấy từ dòng **Số NB bao phòng** ở mục VI của báo cáo khoa. Báo cáo gửi trước khi có dòng này thì cột để trống (không ghi 0); cần đủ số thì trả lại khoa (mục 9.3) để khoa nhập thêm rồi gửi lại.
   - Cột **Số NB ngày hôm trước** đứng trước **Số NB hiện tại**. Số NB hiện tại = Số NB ngày hôm trước + Nhập khoa từ PK + Nhập khoa do CK − Xuất viện − Chuyển khoa; khoa không nhập, phần mềm tính khi khoa lưu báo cáo (mục 9.1). Báo cáo gửi trước khi có cách tính này để trống cột Số NB ngày hôm trước và giữ Số NB hiện tại khoa đã nhập.
   - Cột cuối **Người báo cáo** là người gửi báo cáo của khoa.
5. **Mục C** cộng số liệu thủ thuật các khoa đã gửi. **Mục B**, **mục D** và cột **NB PK chờ nhập viện** là số liệu HIS (mục 9.5).
6. Dưới mỗi mục B, C, D, E, F có dòng **Người báo cáo**, ghi họ tên kèm tên viết tắt của khoa:
   - Mục B, D: người của khoa đã nhập tay số liệu HIS, hoặc người đã bấm **Lấy số liệu HIS** (ghi *lấy từ HIS*).
   - Mục C, E, F: người gửi báo cáo của các khoa có số thủ thuật (mục C), báo **Không ổn** (mục E), có phần nhập thêm (mục F). Không khoa nào có thì ghi *Các khoa đã gửi báo cáo (cột Người báo cáo, mục A)*.
   - Mục chưa có số liệu thì ghi *chưa có*.

   ![Bảng tổng hợp](huong-dan-su-dung/dd-05-tong-hop.png)

   *Hình 9.7. Thẻ **Bảng tổng hợp** của Dashboard ngày HIS lỗi (kéo xuống dưới tám ô chỉ số). Ô đầu thẻ ghi khoa nào đã nhập, khoa nào chưa nhập số liệu HIS, và có các nút **In PDF**, **Xuất Excel**, **Lấy số liệu HIS**, **Khoa báo cáo và số giường** (hai nút sau chỉ có với tài khoản Phòng Điều dưỡng). Mục A ghi tên viết tắt của khoa theo thứ tự Phòng Điều dưỡng xếp (khoa HSTC chưa đặt tên viết tắt nên ghi mã khoa), có cột **Số NB ngày hôm trước**, **Người nước ngoài**, **Bao phòng**, **Chuyển khoa** và **Người báo cáo**.*

7. **Mục E** ghi **Ổn** khi mọi khoa đã gửi đều báo ổn. Có khoa báo sự cố thì ghi **Không ổn** kèm tên viết tắt của khoa, chữ màu đỏ. Dòng **ĐD thực hiện thuốc** của phần tick không có trong mục E (mẫu giấy tổng hợp không có dòng này); xem dòng này trong báo cáo của từng khoa.
8. **Mục F** là phần báo cáo nhập thêm của các khoa (cột **Khoa** ghi tên viết tắt), giữ nguyên xuống dòng như khoa đã ghi. Trong PDF và file Excel, phần nhập thêm dài được chia thành nhiều dòng (mỗi dòng khoảng 1.000 ký tự, chia ở chỗ xuống dòng hoặc giữa hai từ), các dòng sau ghi tên viết tắt kèm **(tiếp)** và tự sang trang, nên in đủ cả phần nhập thêm dài nhất.

   ![Mục E và mục F của bảng tổng hợp](huong-dan-su-dung/dd-06-tong-hop-e-f.png)

   *Hình 9.8. Mục E: một khoa báo sự cố về cung cấp thuốc. Mục F: phần nhập thêm của hai khoa. Dưới mỗi mục là người báo cáo.*

9. Bấm **In PDF** (ô đầu thẻ **Bảng tổng hợp**) để xem và in bảng tổng hợp theo mẫu giấy. Bấm **Xuất Excel** để tải file Excel. Cuối PDF và file Excel có chỗ **Phòng Điều dưỡng** ký, ghi rõ họ tên.

   <img src="huong-dan-su-dung/dd-12-pdf-tong-hop.png" alt="PDF bảng tổng hợp, trang 1" width="560">

   <img src="huong-dan-su-dung/dd-12b-pdf-tong-hop-ky.png" alt="PDF bảng tổng hợp, đầu trang 2: mục F, người báo cáo và chỗ Phòng Điều dưỡng ký" width="560">

   *Hình 9.9. PDF bảng tổng hợp: trang 1 và đầu trang 2. Dưới mỗi mục B–F là người báo cáo; cuối bảng có chỗ Phòng Điều dưỡng ký. Chân trang ghi giờ in, số khoa đã gửi và nguồn số liệu HIS: lấy từ HIS lúc nào, hoặc do khoa nhập tay.*

### 9.3. Xem và trả lại báo cáo cho khoa (Phòng Điều dưỡng)

1. Trong mục A của bảng tổng hợp (Dashboard, thẻ **Bảng tổng hợp**), bấm **tên viết tắt** của khoa. Báo cáo của khoa ngày ở ô **Ngày báo cáo** mở ở bên phải, chỉ xem. Bấm **In PDF** để in báo cáo của khoa.

   ![Báo cáo của một khoa mở từ bảng tổng hợp](huong-dan-su-dung/dd-07-xem-bao-cao-khoa.png)

   *Hình 9.10. Báo cáo của một khoa. Nút **Trả lại khoa** chỉ có khi khoa đã gửi.*

2. Khoa cần sửa: bấm **Trả lại khoa**, ghi lý do, bấm **Trả lại khoa** trong hộp. Báo cáo trở về nháp; khoa thấy ai trả lại và lý do, sửa rồi gửi lại.

   ![Hộp trả lại báo cáo cho khoa](huong-dan-su-dung/dd-08-tra-lai-khoa.png)

   *Hình 9.11. Bắt buộc ghi lý do. Lý do được ghi vào nhật ký và hiện cho khoa.*

### 9.4. Cài khoa báo cáo, thứ tự, tên viết tắt, số giường và khoa nhập số liệu HIS (Phòng Điều dưỡng, làm khi bắt đầu dùng)

1. Trên trang **Dashboard**, thẻ **Bảng tổng hợp**, bấm **Khoa báo cáo và số giường** (ô đầu thẻ).
2. Tick các khoa phải gửi báo cáo, nhập **số giường thực kê** (không tính băng ca).
3. Xếp **thứ tự** các khoa ở mục A của bảng tổng hợp. Các khoa được tick đứng đầu danh sách; cột **Thứ tự** ghi số thứ tự của khoa trên bảng tổng hợp.
   - Gõ số thứ tự mới rồi bấm **Enter** (hoặc bấm ra ngoài ô): khoa chuyển tới vị trí đó, các khoa khác dịch xuống. Gõ số lớn hơn số khoa thì khoa xuống cuối.
   - Bấm mũi tên **↑**, **↓** để đổi chỗ với khoa liền trên, liền dưới.
   - Khoa mới tick xếp cuối. Bỏ tick thì khoa ra khỏi thứ tự.
4. Đặt **tên viết tắt** của khoa ở cột **Tên viết tắt**, ví dụ *Nội TH*, tối đa 20 ký tự. Tên viết tắt in ở cột khoa phòng của mục A, ở mục E, mục F và dòng người báo cáo. Để trống thì in mã khoa (chữ mờ trong ô).
   - Hai khoa không được trùng tên viết tắt, kể cả trùng mã của khoa khác; chữ hoa, chữ thường coi như nhau. Ô bị trùng có viền đỏ và dòng **Trùng với** tên khoa kia. Bấm **Lưu** khi còn trùng thì phần mềm báo lỗi, không lưu gì.
   - Tên viết tắt không đổi khi quản trị lấy lại danh mục khoa/phòng từ HIS (mục 8.2). Phần mềm vẫn dùng mã khoa HIS để ghép số liệu.

   ![Hộp khoa báo cáo và số giường](huong-dan-su-dung/dd-09-khoa-bao-cao.png)

   *Hình 9.12. Các khoa được tick đứng đầu, theo thứ tự in ở mục A. Khoa HSTC để trống tên viết tắt nên bảng tổng hợp in mã khoa. Khoa chưa được tick thì chưa lập được báo cáo và không có dòng trên bảng tổng hợp.*

5. Kéo xuống phần **Khoa nhập số liệu HIS khi HIS lỗi**. Với mỗi chỉ số mục B, D, chọn khoa có nhiệm vụ nhập. Ví dụ: **TSNB đến khám** giao Khoa Khám bệnh; **TS X-quang**, **TS CT Scan** giao Khoa Chẩn đoán hình ảnh. Để trống là chưa giao.
6. Bấm **Lưu**.

   ![Phần giao khoa nhập số liệu HIS](huong-dan-su-dung/dd-09b-giao-khoa-nhap-his.png)

   *Hình 9.13. Giao khoa nhập từng chỉ số mục B, D khi HIS lỗi. Không cần giao **NB PK chờ nhập viện**: mỗi khoa phải báo cáo tự nhập số của khoa mình.*

7. Khi khoa đổi số giường, có khoa mới phải báo cáo, cần đổi thứ tự, tên viết tắt, hoặc đổi khoa nhập số liệu HIS, vào lại đây sửa.
8. Sau khi quản trị **lấy danh mục khoa/phòng từ HIS** (mục 8.2), khoa cũ không còn trên HIS chuyển sang ngừng sử dụng. Trong hộp này, khoa đó có nhãn **Ngừng sử dụng**, nằm cuối danh sách và không chọn được; đầu hộp có khung vàng ghi tên các khoa này. Bấm **Lưu** thì khoa ngừng sử dụng được bỏ khỏi danh sách khoa báo cáo. Chỉ số mục B, D đang giao cho khoa ngừng sử dụng để trống, có dòng nhắc; chọn khoa thay thế (thường là khoa cùng tên trên HIS) rồi lưu.

Người nhập số liệu HIS cho một khoa cần tài khoản thuộc đúng khoa đó, có vai trò **Điều dưỡng** (quyền **Lập và gửi báo cáo điều dưỡng của khoa**). Khoa được giao nhập không cần được tick báo cáo. Quản trị tạo hoặc sửa tài khoản ở mục 8.1.

### 9.5. Số liệu HIS (mục B, mục D, cột "NB PK chờ nhập viện")

**Khi HIS chạy bình thường.** Phòng CNTT cài câu truy vấn số liệu. Phòng Điều dưỡng bấm **Lấy số liệu HIS** (Dashboard, thẻ **Bảng tổng hợp**). Số liệu được lưu theo ngày. Số **NB PK chờ nhập viện** là số tại lúc bấm. Nếu có mã khoa trên HIS không khớp khoa nào, phần mềm báo để phòng CNTT kiểm tra.

**Khi HIS lỗi hoặc chưa cài câu truy vấn.** Nhiệm vụ khoa nào thì khoa đó nhập. Phòng Điều dưỡng không nhập thay, chỉ tổng hợp.

- Khoa phải báo cáo nhập **NB PK chờ nhập viện** của khoa mình.
- Khoa được giao (mục 9.4) nhập các chỉ số mục B, D được giao.

Cách nhập (người của khoa):

1. Bấm **Điều dưỡng** → **Báo cáo của khoa**. Nhập cho ngày trước thì chọn ngày ở góc trên bên phải.
2. Kéo xuống thẻ **Số liệu HIS khoa nhập khi HIS lỗi**, dưới báo cáo. Khoa không phải gửi báo cáo (như Khoa Khám bệnh) thì trang chỉ có thẻ này.
3. Nhập số, bấm **Lưu số liệu HIS**. Dòng cuối thẻ ghi giờ nhập và người nhập. Nhập sai thì sửa rồi lưu lại, kể cả sau khi khoa đã gửi báo cáo.

   ![Khoa Nội nhập NB PK chờ nhập viện](huong-dan-su-dung/dd-10-khoa-nhap-his.png)

   *Hình 9.14. Khoa phải báo cáo nhập **NB PK chờ nhập viện** ở thẻ dưới báo cáo. Thẻ này không bị khóa khi khoa gửi báo cáo.*

   ![Khoa Khám bệnh nhập TSNB đến khám](huong-dan-su-dung/dd-13-khoa-kham-nhap-his.png)

   *Hình 9.15. Khoa Khám bệnh không phải gửi báo cáo, chỉ nhập **TSNB đến khám** được giao.*

Ngày đã có số liệu HIS (lấy được một phần) thì thẻ ghi rõ, và dưới mỗi ô có **Số lấy từ HIS**: chỉ cần nhập ô HIS không có số.

Bảng tổng hợp dùng số như sau:

- Số lấy từ HIS được dùng trước. Ô HIS không có số mới dùng số khoa nhập. Khi HIS chạy lại, bấm **Lấy số liệu HIS**: ô nào HIS có số thì dùng số HIS.
- Số do khoa nhập có dòng chữ nhỏ ghi tên viết tắt (hoặc mã) của khoa đã nhập, ví dụ *KKB nhập tay*. Dòng **Người báo cáo** dưới mục ghi người đã nhập.
- Chưa có khoa nào nhập thì ô để trống. Xem dòng **Khoa nhập tay khi HIS lỗi** ở ô đầu thẻ **Bảng tổng hợp** (Hình 9.7) để biết khoa nào chưa nhập và nhắc khoa đó.

![Mục B, C, D với số khoa nhập tay](huong-dan-su-dung/dd-05b-tong-hop-b-d.png)

*Hình 9.16. Mục B và mục D ghi tên viết tắt (hoặc mã) của khoa đã nhập từng số; dòng người báo cáo ghi người đã nhập. Hai dòng siêu âm tim và điện tâm đồ để trống vì khoa được giao chưa nhập.*

### 9.6. Dashboard điều dưỡng: ô chỉ số và biểu đồ (Phòng Điều dưỡng)

Trang **Dashboard** là trang làm việc hằng ngày của Phòng Điều dưỡng: bảng tổng hợp của một ngày (thẻ **Bảng tổng hợp**, mục 9.2) và số liệu nhiều ngày bằng các ô chỉ số và biểu đồ (thẻ **Biểu đồ**). Số liệu lấy từ báo cáo các khoa **đã gửi**, như bảng tổng hợp: khoa chưa gửi không được tính. Trang không sửa số liệu; số của khoa nào sai thì trả lại báo cáo cho khoa đó (mục 9.3, hoặc bước 6 dưới đây).

1. Bấm **Điều dưỡng** → **Dashboard**. Chọn ngày ở ô **Ngày báo cáo** (mặc định hôm nay).
2. Phần trên cùng là **tám ô chỉ số của ngày đã chọn**, luôn hiện ở cả hai thẻ:
   - **Khoa đã gửi báo cáo**, kèm tên viết tắt các khoa chưa gửi để nhắc.
   - **Số NB hiện tại**, tăng hay giảm bao nhiêu so với ngày hôm trước (tính trên các báo cáo có Số NB ngày hôm trước).
   - **Công suất giường**: Số NB hiện tại chia số giường của các khoa đã gửi. Vượt 100% có biểu tượng cảnh báo và chữ **Vượt số giường**.
   - **NB chăm sóc cấp 1**, kèm số NB thở máy, theo dõi monitor, thở oxy.
   - **Nhập khoa**, **Xuất viện**, **Chuyển khoa**: số ca của ngày hôm trước.
   - **Khoa báo "Không ổn"**, kèm số ca ADR và số báo cáo có nội dung bất thường (phần nhập thêm).
3. Bấm thẻ **Biểu đồ**. Chọn khoảng thời gian **7 ngày**, **30 ngày** hoặc **90 ngày** ở góc phải của thẻ; kỳ là các ngày đến hết ngày ở ô **Ngày báo cáo** (dòng *Từ … đến …* ghi rõ). Đổi ngày hoặc khoảng thời gian thì số cũ mờ đi trong lúc tải số mới. Đổi khoảng thời gian không tải lại bảng tổng hợp.

   ![Dashboard điều dưỡng: ô chỉ số và thẻ Biểu đồ](huong-dan-su-dung/dd-14-dashboard.png)

   *Hình 9.17. Dashboard, thẻ **Biểu đồ**, 30 ngày. Tám ô chỉ số ở trên hai thẻ. Rê chuột vào biểu đồ để xem số của từng ngày: khung gợi ý ghi cả số khoa đã gửi và công suất giường của ngày đó.*

4. Bốn **biểu đồ theo ngày**: Số NB hiện tại; nhập khoa, xuất viện, chuyển khoa; NB chăm sóc cấp 1, thở máy, monitor; số khoa đã gửi báo cáo (đường đứt nét là số khoa phải báo cáo). Rê chuột, hoặc bấm vào biểu đồ rồi dùng phím mũi tên trái, phải, để xem số từng ngày. Bấm **Xem bảng** để đọc số dạng bảng (ngày mới nhất ở trên).
5. Phần dưới của thẻ **Biểu đồ**:
   - **Công suất giường theo khoa** của ngày cuối, khoa cao nhất ở trên.
   - **"Không ổn" theo nội dung**: số lượt khoa báo "Không ổn" trong cả kỳ theo từng dòng của phần tick (mỗi khoa, mỗi ngày, mỗi dòng tính một lượt), kèm khoa báo trong ngày cuối. Có tính cả dòng **ĐD thực hiện thuốc** (bảng tổng hợp giấy không có dòng này).
   - **Theo khoa**: số liệu từng khoa ngày cuối, và số ngày khoa đã gửi báo cáo trong kỳ.

   ![Dashboard điều dưỡng: công suất giường theo khoa, "Không ổn" theo nội dung và bảng theo khoa](huong-dan-su-dung/dd-14b-dashboard-khoa.png)

   *Hình 9.18. Phần dưới thẻ **Biểu đồ**. Khoa vượt số giường có biểu tượng cảnh báo; khoa chưa gửi ghi **Chưa gửi**. Tên viết tắt của khoa ở bảng **Theo khoa** (chữ xanh) bấm được.*

6. Ở bảng **Theo khoa**, bấm **tên viết tắt** của khoa. Báo cáo của khoa **ngày cuối** (ngày ở ô **Ngày báo cáo**) mở ở bên phải, chỉ xem, giống khi bấm ở mục A của bảng tổng hợp (Hình 9.10).
   - Bấm **In PDF** để xem và in báo cáo của khoa.
   - Khoa cần sửa: bấm **Trả lại khoa**, ghi lý do, bấm **Trả lại khoa** trong hộp (Hình 9.11). Nút này chỉ có khi khoa đã gửi, và chỉ với tài khoản được trả lại báo cáo (vai trò Phòng Điều dưỡng). Dashboard tính lại ngay, cả ô chỉ số, biểu đồ và bảng tổng hợp: khoa đó thành **Chưa gửi** cho tới khi khoa sửa và gửi lại.
   - Khoa chưa lập báo cáo ngày đó thì ghi **Khoa chưa lập báo cáo ngày này.**; khoa đang làm dở thì thấy báo cáo nháp.
   - Muốn xem báo cáo của ngày khác: đổi ngày ở ô **Ngày báo cáo** rồi bấm lại tên khoa.

Lưu ý khi đọc:

- Ngày ít khoa gửi báo cáo thì tổng Số NB hiện tại thấp hơn thực tế. Xem biểu đồ **Khoa đã gửi báo cáo** cùng ngày đó.
- Công suất giường dùng số giường đang cài trong hộp **Khoa báo cáo và số giường** (mục 9.4), kể cả khi xem ngày cũ.

### 9.7. Ủy quyền báo cáo khi vắng

Khi vắng (nghỉ phép, đi học, đi công tác…), người lập báo cáo của khoa **ủy quyền** cho người khác làm thay trong một khoảng ngày. Người được ủy quyền chỉ làm được **trong đúng thời gian đó** và **đúng nội dung được ủy quyền**. Bảng tổng hợp ghi rõ báo cáo nào do người được ủy quyền làm, được ai ủy quyền.

**Người ủy quyền (người lập báo cáo của khoa):**

1. Bấm **Điều dưỡng** → **Ủy quyền báo cáo**, rồi bấm **Ủy quyền mới**.
2. Điền hộp **Ủy quyền báo cáo điều dưỡng**:
   - **Khoa**: khoa của mình (tài khoản thuộc nhiều khoa chọn khoa cần ủy quyền).
   - **Người được ủy quyền**: gõ tên để tìm. Danh sách chỉ có tài khoản đang dùng, thuộc một khoa, được quyền lập báo cáo điều dưỡng (điều dưỡng cùng khoa hoặc khoa khác).
   - **Thời gian ủy quyền**: từ ngày, đến ngày, tính cả hai ngày; bắt đầu từ hôm nay trở đi, tối đa 366 ngày.
   - **Nội dung ủy quyền**: **Báo cáo của khoa** (lập, lưu nháp, gửi báo cáo hằng ngày), **Số liệu HIS khi HIS lỗi** (nhập số liệu HIS của khoa, mục 9.5), hoặc cả hai.
   - **Lý do** (không bắt buộc), ví dụ "Nghỉ phép".
3. Bấm **Ủy quyền**.

   ![Hộp Ủy quyền mới: khoa, người được ủy quyền, thời gian, nội dung, lý do](huong-dan-su-dung/dd-15-uy-quyen-moi.png)

   *Hình 9.19. Điều dưỡng Khoa Nhi ủy quyền cho điều dưỡng Khoa Ngoại tổng hợp làm báo cáo và nhập số liệu HIS của Khoa Nhi trong hai ngày.*

4. Ủy quyền hiện ở bảng **Tôi đã ủy quyền**, có trạng thái **Sắp tới** (chưa tới ngày bắt đầu), **Đang hiệu lực**, **Đã hết hạn** hoặc **Đã hủy**. Đi làm lại sớm hoặc ủy quyền nhầm thì bấm **Hủy ủy quyền**: người được ủy quyền không làm được nữa; báo cáo đã gửi theo ủy quyền vẫn giữ nguyên và vẫn ghi là được ủy quyền.

**Người được ủy quyền:**

1. Bấm **Điều dưỡng** → **Ủy quyền báo cáo**. Bảng **Tôi được ủy quyền** liệt kê các ủy quyền giao cho mình; ủy quyền đang hiệu lực có nút **Mở báo cáo của khoa**.

   ![Trang Ủy quyền của người được ủy quyền: ủy quyền đang hiệu lực có nút Mở báo cáo của khoa](huong-dan-su-dung/dd-15b-duoc-uy-quyen.png)

   *Hình 9.20. Điều dưỡng Khoa Nội được Khoa Nhi ủy quyền (đang hiệu lực); một ủy quyền khác đã bị hủy.*

2. Bấm **Mở báo cáo của khoa**, hoặc vào **Báo cáo của khoa** rồi chọn khoa có chữ **(được ủy quyền)** ở ô khoa đầu trang. Dòng xanh trên cùng ghi đang làm thay ai, thời gian và nội dung được ủy quyền. Ô ngày chỉ chọn được các ngày trong thời gian ủy quyền.

   ![Báo cáo của khoa khi làm thay theo ủy quyền](huong-dan-su-dung/dd-15c-bao-cao-thay.png)

   *Hình 9.21. Điều dưỡng Khoa Nội làm báo cáo của Khoa Nhi theo ủy quyền.*

3. Lập, lưu nháp và gửi báo cáo như mục 9.1; khi HIS lỗi, nhập số liệu HIS của khoa như mục 9.5. Người chỉ được ủy quyền số liệu HIS thì chỉ thấy thẻ số liệu HIS, không xem được báo cáo của khoa.
4. Hết thời gian ủy quyền hoặc ủy quyền bị hủy thì khoa đó không còn trong ô khoa, và phần mềm không cho xem, sửa báo cáo của khoa đó nữa, kể cả báo cáo của các ngày trong thời gian ủy quyền. Báo cáo đã gửi cần sửa thì Phòng Điều dưỡng **Trả lại khoa** (mục 9.3) để người của khoa sửa.

**Phòng Điều dưỡng:**

- Bảng tổng hợp (Dashboard, thẻ **Bảng tổng hợp**, mục 9.2) có ô **Ủy quyền báo cáo trong ngày**: khoa nào, ai làm thay, được ai ủy quyền, thời gian và nội dung. Báo cáo gửi theo ủy quyền ghi "Họ tên (được … ủy quyền)" ở cột **Người báo cáo**; dòng **Người báo cáo** dưới các mục B–F cũng ghi như vậy. PDF và Excel ghi giống màn hình.

   ![Bảng tổng hợp: ô Ủy quyền báo cáo trong ngày và cột Người báo cáo ghi được ủy quyền](huong-dan-su-dung/dd-15d-tong-hop-uy-quyen.png)

   *Hình 9.22. Báo cáo Khoa Nhi do Điều dưỡng Demo gửi theo ủy quyền của Điều dưỡng Nhi Demo.*

- Trang **Ủy quyền báo cáo** của Phòng Điều dưỡng có bảng **Mọi ủy quyền** của các khoa. Tài khoản có quyền quản lý báo cáo điều dưỡng (vai trò Phòng Điều dưỡng) hủy được ủy quyền chưa hết hạn.

   ![Mọi ủy quyền của các khoa](huong-dan-su-dung/dd-15e-moi-uy-quyen.png)

   *Hình 9.23. Phòng Điều dưỡng xem mọi ủy quyền: đang hiệu lực, sắp tới, đã hủy.*

---

<a id="kiem-thu"></a>

## 10. Kiểm thử tính năng mới

Mỗi bản cài mới được kiểm thử trên **môi trường kiểm thử UAT** (đầu trang có dải vàng **UAT**) trước khi đưa vào chạy thật (quyết định 50). Người quản lý kiểm thử **phân công từng tính năng mới cho từng người, mỗi người một khoảng thời gian**; người được phân công kiểm thử rồi **bắt buộc gửi báo cáo**. Báo cáo là căn cứ để sửa, nâng cấp phần mềm và quyết định đưa bản mới lên chạy thật (quyết định 49).

| Ai | Làm gì | Ở đâu |
|---|---|---|
| Người quản lý kiểm thử: vai trò **Quản trị hệ thống**, hoặc vai trò được thêm quyền *Phân công kiểm thử, xem và xử lý báo cáo kiểm thử* (mục 8.3) | Phân công, theo dõi, xem báo cáo, ghi hướng xử lý, xuất Excel | **Quản trị** → **Phân công kiểm thử** |
| Người được phân công (tài khoản bất kỳ) | Xem việc, kiểm thử, gửi báo cáo | Dòng nhắc trên trang **Tổng quan**, hoặc menu tài khoản (góc phải) → **Việc kiểm thử của tôi** |

### 10.1. Phân công kiểm thử (người quản lý)

1. Bấm **Quản trị** → **Phân công kiểm thử** → **Phân công mới**.
2. Điền:
   - **Đợt kiểm thử**: tên bản cài hoặc gói, ví dụ "Gói 72 – 25/11/2026". Gõ vài chữ để chọn lại đợt đã có.
   - **Tính năng cần kiểm thử**.
   - **Nội dung, cách kiểm thử**: các bước làm, dữ liệu dùng thử (giả lập), kết quả mong đợi.
   - **Người kiểm thử và thời gian của từng người**: chọn người (gõ tên để tìm), chọn **từ ngày** và **hạn báo cáo**. Bấm **Thêm người kiểm thử** để thêm người; mỗi người một thời gian riêng.
3. Bấm **Phân công**. Mỗi người có một việc riêng.

   ![Hộp Phân công kiểm thử: đợt, tính năng, cách kiểm thử, người kiểm thử và thời gian của từng người](huong-dan-su-dung/kt-01-phan-cong.png)

   *Hình 10.1. Phân công kiểm thử tính năng **Kiosk: quét CCCD bằng camera** cho hai người, mỗi người một thời gian.*

4. Trang **Phân công kiểm thử** có:
   - **Tổng hợp theo tính năng**: số người, số **Đạt**, **Đạt một phần**, **Không đạt**, **Chưa báo cáo** (kèm số quá hạn).
   - **Phân công**: từng người, thời gian, trạng thái (**Chưa tới ngày**, **Đang kiểm thử**, **Quá hạn**, **Đã báo cáo**, **Trễ hạn** khi gửi sau hạn, **Đã hủy**) và kết quả.

   Chọn đợt ở ô đầu trang để chỉ xem một đợt.

   ![Trang Phân công kiểm thử: tổng hợp theo tính năng và bảng phân công](huong-dan-su-dung/kt-02-tong-hop.png)

   *Hình 10.2. Tổng hợp một đợt kiểm thử: tính năng nào đã đạt, tính năng nào còn lỗi, ai chưa báo cáo.*

5. **Sửa** hoặc **Hủy** phân công được khi người kiểm thử chưa gửi báo cáo.

### 10.2. Kiểm thử và gửi báo cáo (người được phân công)

1. Trang **Tổng quan** nhắc *"Bạn có … việc kiểm thử chưa báo cáo (… quá hạn)"*. Bấm **Xem và báo cáo**, hoặc menu tài khoản → **Việc kiểm thử của tôi**.

   ![Trang Tổng quan nhắc việc kiểm thử chưa báo cáo](huong-dan-su-dung/kt-03-nhac-viec.png)

   *Hình 10.3. Dòng nhắc trên trang **Tổng quan**: bấm **Xem và báo cáo** để mở việc kiểm thử của mình.*

2. Mỗi việc ghi tính năng, đợt kiểm thử, thời gian và cách kiểm thử. Làm theo cách kiểm thử trên môi trường UAT.
3. Bấm **Gửi báo cáo**:
   - **Kết quả**: **Đạt**, **Đạt một phần** hoặc **Không đạt**.
   - **Lỗi, vấn đề phát hiện**: bắt buộc khi chưa đạt. Ghi cách làm để gặp lỗi, màn hình, thông báo hiện ra. Không ghi thông tin người bệnh thật.
   - **Đề xuất nâng cấp** (không bắt buộc).

   ![Hộp Báo cáo kiểm thử: kết quả, lỗi phát hiện, đề xuất nâng cấp](huong-dan-su-dung/kt-04-bao-cao.png)

   *Hình 10.4. Báo cáo kiểm thử "Không đạt" phải ghi lỗi phát hiện.*

4. Chỉ gửi được báo cáo từ ngày bắt đầu kiểm thử. Gửi sau hạn vẫn được nhưng ghi **Trễ hạn**. Sửa được báo cáo (nút **Sửa báo cáo**) cho tới khi người quản lý ghi hướng xử lý; sau đó việc đó hiện hướng xử lý.

### 10.3. Xem báo cáo, ghi hướng xử lý, xuất Excel (người quản lý)

1. Ở dòng đã báo cáo, bấm **Xem báo cáo**: kết quả, lỗi phát hiện, đề xuất.
2. Ghi **Hướng xử lý** (ví dụ "Sửa trong gói 73", "Giữ nguyên: đúng quy trình") rồi bấm **Lưu hướng xử lý**. Người kiểm thử thấy hướng xử lý và không sửa báo cáo được nữa.
3. Bấm **Xuất Excel** ở đầu trang: mọi phân công và báo cáo của đợt đang chọn (người, thời gian, trạng thái, kết quả, lỗi, đề xuất, giờ gửi, trễ hạn, hướng xử lý), gửi nhóm phát triển để sửa và nâng cấp.
4. Khi mọi tính năng của đợt đã **Đạt** (hoặc lỗi còn lại đã có hướng xử lý được chấp nhận), phòng CNTT cài **đúng gói đó** lên môi trường chạy thật ([hướng dẫn triển khai, mục 5.10](trien-khai.md)).

---

<a id="noi-kiem"></a>

## 11. Sổ nội kiểm đường huyết tại giường

Phần mềm thay **"Sổ theo dõi kết quả nội kiểm đường huyết tại giường"** giấy (mẫu CT-ĐHTG/BM01, lần ban hành 03) (quyết định 57). Mỗi máy đo của một khoa có **một sổ mỗi tháng**, mỗi ngày một dòng như sổ giấy. Khoa ghi kết quả; **Khoa Huyết học – Sinh hóa (Khoa HSH)** xem xét từng ngày và xác nhận sổ cuối tháng.

| Ai | Làm gì | Ở đâu |
|---|---|---|
| Khoa HSH: vai trò **Khoa HSH (nội kiểm)** | Khai báo máy đo, lô que thử, lô dung dịch chứng; xem xét từng ngày; trả lại ngày hoặc sổ; xác nhận sổ tháng; in PDF | **Nội kiểm** → **Máy đo và lô vật tư**, **Sổ nội kiểm** |
| Điều dưỡng khoa (vai trò có quyền *Ghi kết quả nội kiểm đường huyết của khoa, gửi sổ tháng*, mục 8.3) | Mở sổ tháng, ghi kết quả từng ngày, ghi nguyên nhân và kết quả chạy lại, ghi điều chỉnh, gửi sổ tháng, in PDF | **Nội kiểm** → **Sổ nội kiểm** |

**Dữ liệu không bị sửa, có xác nhận giữa hai khoa:**

- Phần mềm **tự đánh giá** Đạt/Không đạt theo ngưỡng chấp nhận của lô dung dịch chứng (tính cả hai đầu ngưỡng).
- Kết quả đã ghi **không sửa được**. Ghi sai thì **ghi điều chỉnh** (bắt buộc lý do): kết quả cũ vẫn giữ, hiện và in **gạch ngang** kèm lý do, người điều chỉnh.
- Khoa HSH đã xem xét ngày nào thì khoa **không ghi, không điều chỉnh** ngày đó nữa. Cần sửa thì Khoa HSH **trả lại ngày** (bắt buộc lý do), khoa điều chỉnh, Khoa HSH xem xét lại. Lần xem xét cũ vẫn giữ (gạch ngang).
- Khoa **gửi sổ tháng** thì không ghi thêm được. Khoa HSH **xác nhận sổ tháng** thì sổ khóa hẳn.
- Người ghi kết quả của một ngày **không tự xem xét** ngày đó; người gửi sổ **không tự xác nhận** sổ.
- Mọi lần ghi, điều chỉnh, xem xét, trả lại, gửi, xác nhận được ghi **Nhật ký hệ thống** (mục 7.1). Không có nút xóa.

### 11.1. Khai báo máy đo và lô vật tư (Khoa HSH, làm trước)

1. Bấm **Nội kiểm** → **Máy đo và lô vật tư**.
2. Tab **Máy đo** → **Thêm máy đo**: chọn **khoa dùng máy**, nhập **loại máy** (hãng, đời máy) và **số seri** (không trùng máy khác). Máy hỏng, thu hồi: bấm **Sửa**, tắt **Đang dùng**. Chuyển máy sang khoa khác: **Sửa**, chọn khoa mới; sổ tháng này của khoa cũ giữ nguyên, khoa mới mở sổ mới.
3. Tab **Lô que thử, dung dịch chứng** → **Thêm lô**:
   - **Que thử**: tên vật tư, số lô, hạn dùng.
   - **Dung dịch chứng**: thêm **mức** (Level 1, 2 hoặc 3) và **ngưỡng chấp nhận** (từ – đến, đơn vị mg/dL hoặc mmol/L), lấy theo tờ hướng dẫn của lô. Cùng số lô nhưng khác mức thì khai báo hai lần.

   ![Danh mục lô que thử, dung dịch chứng](huong-dan-su-dung/nk-01-danh-muc.png)

   *Hình 11.1. Danh mục lô: que thử, dung dịch chứng Level 1 và Level 2 kèm ngưỡng chấp nhận (dữ liệu giả lập).*

   ![Hộp Thêm lô dung dịch chứng: mức, hạn dùng, ngưỡng chấp nhận, đơn vị](huong-dan-su-dung/nk-02-them-lo.png)

   *Hình 11.2. Thêm lô dung dịch chứng Level 3.*

4. Lô hết dùng: **Sửa**, tắt **Đang dùng**; khoa không chọn được lô đó nữa. Sửa lô (ví dụ ngưỡng) chỉ áp dụng cho kết quả ghi **sau** khi sửa; kết quả đã ghi giữ ngưỡng lúc ghi. Lô **hết hạn** trước ngày thực hiện thì khoa không ghi được kết quả với lô đó.

### 11.2. Mở sổ tháng (khoa)

1. Bấm **Nội kiểm** → **Sổ nội kiểm**. Chọn **tháng** ở đầu trang (mặc định tháng này). Danh sách có máy của khoa mình, trạng thái sổ, số ngày đã ghi, không đạt, Khoa HSH đã xem xét, còn chờ xem xét.
2. Máy chưa có sổ tháng này: bấm **Mở sổ**. Sổ đã có: bấm **Xem sổ**. Mở được sổ của 12 tháng gần đây (ghi bù từ sổ giấy).

   ![Danh sách sổ nội kiểm tháng của khoa](huong-dan-su-dung/nk-03-danh-sach-so.png)

   *Hình 11.3. Danh sách sổ tháng 11 của khoa Nội: 21 ngày đã ghi, 1 ngày không đạt, Khoa HSH đã xem xét 15 ngày.*

### 11.3. Ghi kết quả hằng ngày (khoa)

1. Ở dòng ngày thực hiện, bấm **Ghi kết quả**.
2. **Lô que thử, ngày mở hộp, lô dung dịch chứng hai mức, ngày mở lọ** được điền sẵn theo lần ghi trước của máy. Mở hộp que, lọ dung dịch chứng mới thì chọn lô mới và ngày mở mới. Mức thấp hơn ở dòng đầu (như hai cột kết quả của sổ giấy).
3. Nhập **kết quả** hai mức. Đánh giá **Đạt/ Pass** hoặc **Không đạt/ Fail** hiện ngay bên cạnh.
4. Bấm **Lưu kết quả**. Nhân viên thực hiện là người đăng nhập. Ghi sau ngày thực hiện (ghi bù) thì dòng đó có nhãn **Ghi muộn** (PDF có dấu `*`).

   ![Hộp Ghi kết quả nội kiểm: lô điền sẵn, đánh giá hiện ngay](huong-dan-su-dung/nk-04-ghi-ket-qua.png)

   *Hình 11.4. Mức 2 ngoài ngưỡng 100–140 mg/dL: phần mềm đánh giá **Không đạt**.*

5. Kết quả **không đạt**: bấm **Ghi nguyên nhân, chạy lại**, ghi **nguyên nhân không đạt** (bắt buộc), nhập **kết quả chạy lại** hai mức (đánh giá lại hiện ngay). Không chạy lại được thì bỏ chọn **Đã chạy lại QC** và ghi nguyên nhân. Mỗi kết quả ghi nguyên nhân và chạy lại một lần.

   ![Hộp ghi nguyên nhân không đạt và kết quả chạy lại](huong-dan-su-dung/nk-05-chay-lai.png)

   *Hình 11.5. Ghi nguyên nhân, kết quả chạy lại đạt.*

### 11.4. Ghi sai: ghi điều chỉnh (khoa)

1. Ở dòng kết quả ghi sai, bấm **Ghi điều chỉnh** (chỉ có khi Khoa HSH **chưa xem xét** ngày đó và sổ chưa gửi).
2. Ghi **lý do điều chỉnh** (bắt buộc). Kết quả cũ được điền sẵn: sửa chỗ sai (kết quả, lô, ngày mở, cả ngày thực hiện nếu ghi nhầm ngày) rồi bấm **Lưu điều chỉnh**. Ghi nhầm máy (không phải của máy này): đánh dấu **Chỉ hủy kết quả này**.

   ![Hộp Ghi điều chỉnh: lý do bắt buộc, kết quả cũ được giữ lại](huong-dan-su-dung/nk-06-dieu-chinh.png)

   *Hình 11.6. Ghi điều chỉnh kết quả mức 1 do đọc nhầm số trên máy.*

3. Kết quả cũ vẫn hiện trên sổ: chữ xám **gạch ngang**, kèm *Đã hủy: lý do (người điều chỉnh)*; kết quả mới ngay dưới.

   ![Sổ tháng của khoa: kết quả đã hủy gạch ngang, kết quả không đạt có nguyên nhân và chạy lại](huong-dan-su-dung/nk-07-so-thang.png)

   *Hình 11.7. Ngày 20 và 25: kết quả ghi sai gạch ngang, kèm lý do; ngày 26 không đạt, đã ghi nguyên nhân và chạy lại đạt.*

4. Ngày Khoa HSH đã xem xét thì không còn nút **Ghi điều chỉnh**: báo Khoa HSH **trả lại ngày** (mục 11.5).

### 11.5. Xem xét từng ngày (Khoa HSH)

1. Bấm **Nội kiểm** → **Sổ nội kiểm**, chọn tháng (chọn khoa ở ô **Khoa** nếu cần), bấm **Xem sổ** ở dòng có số ngày **Chờ xem xét**.
2. Xem xét một ngày: bấm **Xem xét** ở dòng ngày đó. Xem xét nhiều ngày: đánh dấu các ngày (hoặc bấm **Chọn mọi ngày chờ xem xét**: chọn các ngày đã qua và các ngày đã có kết quả, không chọn hôm nay khi khoa chưa ghi), bấm **Xem xét … ngày đã chọn**.
3. Mỗi ngày chọn kết quả xem xét, như cột cuối của sổ giấy (điền sẵn theo gợi ý của phần mềm, chỉ có các lựa chọn khớp với kết quả khoa đã ghi):
   - **Đã kiểm - ĐẠT**: kết quả đạt, hoặc không đạt nhưng chạy lại đạt.
   - **Đã kiểm - Không có QC ngày này**: ngày khoa không ghi kết quả.
   - **Đã kiểm - KHÔNG ĐẠT, KHÔNG CHẠY LẠI QC**: không đạt, không chạy lại (hoặc chạy lại vẫn không đạt).
   - **Đã kiểm - KHÔNG ĐẠT, đã chạy lại QC sau nhắc nhở**: không đạt, khoa đã chạy lại sau khi Khoa HSH nhắc.

   Ghi chú (không bắt buộc) rồi bấm **Lưu xem xét**.

   ![Hộp xem xét nhiều ngày: kết quả khoa ghi và kết quả xem xét điền sẵn theo gợi ý](huong-dan-su-dung/nk-08-xem-xet.png)

   *Hình 11.8. Xem xét 11 ngày một lần (hôm nay khoa chưa ghi nên không được chọn sẵn); ngày 18 không đạt, không chạy lại.*

4. Người ghi kết quả (hoặc ghi chạy lại) của một ngày không tự xem xét ngày đó, kể cả khi có cả hai quyền.
5. **Trả lại ngày**: ở ngày đã xem xét, bấm **Trả lại ngày**, ghi lý do. Khoa ghi điều chỉnh ngày đó, rồi Khoa HSH xem xét lại. Sổ đã gửi thì sổ cũng được trả lại khoa.

### 11.6. Gửi sổ tháng, xác nhận sổ tháng

1. **Khoa**: từ **ngày cuối tháng**, bấm **Gửi sổ tháng** ở đầu trang sổ. Gửi rồi khoa không ghi thêm, không điều chỉnh được.
2. **Khoa HSH**: xem xét đủ **mọi ngày** trong tháng, rồi bấm **Xác nhận sổ tháng**. Sổ **khóa hẳn**, không ai thay đổi được nữa. Người gửi sổ không tự xác nhận được.
3. Sổ cần sửa: Khoa HSH bấm **Trả lại sổ** (ghi lý do). Khoa thấy lý do ở đầu trang sổ, ghi thêm hoặc điều chỉnh (với ngày đã xem xét thì Khoa HSH trả lại ngày đó), rồi gửi lại.

   ![Sổ tháng 10 đã được Khoa HSH xác nhận](huong-dan-su-dung/nk-09-xac-nhan.png)

   *Hình 11.9. Sổ đã xác nhận: ghi người xác nhận, thời điểm; không còn nút thao tác.*

### 11.7. In sổ theo mẫu giấy

Bấm **In PDF** ở đầu trang sổ (khoa và Khoa HSH đều in được). PDF khổ A4 ngang theo mẫu CT-ĐHTG/BM01: tên đơn vị, logo (lấy ở *Cấu hình hệ thống*), mã số mẫu; khoa, tháng, máy; que thử và dung dịch chứng đã dùng trong tháng (số lô, hạn dùng, ngày mở, ngưỡng chấp nhận; nhiều lô thì nối bằng " - " như sổ giấy); mỗi ngày một dòng. Kết quả đã hủy và lần xem xét đã trả lại in chữ xám **gạch ngang** kèm lý do. Cuối trang ghi trạng thái sổ (đang ghi, đã gửi, đã xác nhận: người, thời điểm).

![PDF sổ nội kiểm tháng 10 theo mẫu giấy](huong-dan-su-dung/nk-10-pdf.png)

*Hình 11.10. Trang đầu PDF sổ tháng 10 (đã xác nhận); ngày 9 không đạt, đã chạy lại.*

![PDF sổ tháng 11, trang 2: kết quả đã hủy in gạch ngang kèm lý do](huong-dan-su-dung/nk-11-pdf-dieu-chinh.png)

*Hình 11.11. Trang 2 PDF sổ tháng 11: ngày 20 và 25 có kết quả đã hủy (gạch ngang, kèm lý do), ngày 26 không đạt, đã chạy lại.*

---

<a id="hoi-dap"></a>

## 12. Câu hỏi thường gặp

**Tôi quên mật khẩu thì làm sao?**
Liên hệ bộ phận CNTT. Quản trị đặt lại mật khẩu tạm (mục 8.1). Bạn đăng nhập bằng mật khẩu tạm rồi đổi mật khẩu mới.

**Tài khoản báo bị tạm khóa?**
Do nhập sai mật khẩu 5 lần. Chờ hết thời gian khóa (mặc định 15 phút), hoặc nhờ quản trị **Mở khóa**.

**Màn hình báo "Hệ thống đang bảo trì"?**
Quản trị đang bảo trì hoặc phòng CNTT đang cài bản mới. Không cần tải lại trang hay đăng nhập lại: chờ đến giờ dự kiến xong, màn hình tự đóng (hoặc tự tải lại trang) khi xong. Dữ liệu đang nhập vẫn giữ trên màn hình. Xem mục 8.7.

**Vì sao tôi không thấy nút "Lấy thông tin"?**
Nút này chỉ có khi bệnh viện đã kết nối hệ thống thông tin người bệnh. Nếu chưa có, hãy quét CCCD hoặc nhập trực tiếp.

**Người bệnh không có CCCD thì sao?**
Nhập trực tiếp vào phần **2. Thông tin người bệnh** ở trang **Tiếp nhận người bệnh**. Số CCCD không bắt buộc.

**Camera không bật khi bấm "Quét bằng camera"?**
Hộp thoại ghi lý do:

- *"Đang mở camera…"* mãi không đổi: trình duyệt đang chờ trả lời câu hỏi quyền dùng camera ở góc trên màn hình. Chọn **Cho phép**.
- *"…chỉ hoạt động khi truy cập qua HTTPS"*: phần mềm đang mở bằng địa chỉ `http://`. Mở bằng địa chỉ `https://` của phần mềm.
- *"Bạn chưa cho phép trang này dùng camera"*: bấm biểu tượng bên trái thanh địa chỉ → **Camera** → **Cho phép**, rồi tải lại trang.
- *"Không tìm thấy camera"*: máy không có webcam hoặc webcam chưa nhận.
- *"Camera đang được ứng dụng khác sử dụng"*: đóng Zalo, Teams, Zoom… rồi thử lại.

Trong lúc chưa dùng được camera: dùng máy quét 2D hoặc nhập tay. Báo bộ phận CNTT nếu vẫn lỗi.

**Camera bật nhưng lâu không nhận mã QR?**
Mã QR trên thẻ CCCD nhỏ (khoảng 1,4 cm), camera phải thấy rõ từng ô của mã:

- Đưa mã QR vào giữa **khung vuông**, cách camera khoảng một gang tay, giữ yên 1–2 giây.
- Hình bị mờ: đưa thẻ ra xa hơn một chút (nhiều webcam không lấy nét được khi để quá gần). Mã quá nhỏ trong khung vuông: đưa lại gần hơn.
- Mặt thẻ bị lóa đèn: nghiêng thẻ một chút. Chỗ tối: bật thêm đèn.
- Mã QR trên VNeID: tăng độ sáng màn hình điện thoại, tránh để màn hình lóa đèn.
- Webcam đời cũ (độ phân giải thấp, không tự lấy nét) có thể vẫn khó đọc mã trên thẻ: dùng máy quét 2D (nhanh và chắc chắn nhất) hoặc nhập tay, và báo bộ phận CNTT.

**Người bệnh có ký trên điện thoại riêng của họ được không?**
Được. Bấm **Mã QR cho người bệnh** (hoặc **Mã QR ký cả bộ**), người bệnh quét mã, nhập ngày sinh của người bệnh rồi ký (mục 2.8, cách 2 và mục 3.1). Điện thoại phải vào được địa chỉ của phần mềm, ví dụ qua Wi-Fi của bệnh viện.

**Người bệnh quét mã QR nhưng điện thoại không mở được trang?**
Thường do điện thoại không vào được mạng của bệnh viện (đang dùng 4G). Nhờ người bệnh bắt Wi-Fi của bệnh viện rồi quét lại, hoặc dùng cách 1 (ký trên máy của bệnh viện).

**Lỡ tải lại trang khi người bệnh đang ký?**
Phiên ký kết thúc, màn hình báo *"Không có phiên ký đang mở"*. Bấm **Đăng nhập**, mở lại phiếu, bấm **Ký trên điện thoại** (hoặc **Ký cả bộ trên điện thoại**) để mở phiên mới. Phiếu chưa bị mất gì.

**Muốn sửa phiếu đã trình ký?**
Nếu phiếu chưa hoàn tất: bấm **Trả về nháp**, sửa, rồi trình ký lại. Chữ ký của người bệnh (nếu đã ký) bị bỏ, phải ký lại. Nếu phiếu đã hoàn tất: nhờ trưởng khoa thu hồi rồi lập phiếu thay thế (mục 4.1, 4.2).

**Vì sao tôi không thấy nút xác nhận trên phiếu?**
Kiểm tra: bạn có đúng vai trò của bước đang chờ không; các bước trước đã xác nhận xong chưa; bạn đã xác nhận một bước khác trên phiếu này chưa (mỗi bước một người khác nhau); phiếu có thuộc khoa của bạn không. Khung xanh trên trang phiếu ghi rõ đang chờ vai trò nào.

**Phiếu có trong "Chờ tôi xác nhận" nhưng mở ra không có nút xác nhận?**
Danh sách tính theo bước đang chờ của từng phiếu. Nếu vừa có người khác xác nhận bước đó (hoặc phiếu vừa được trả về nháp), hãy tải lại trang Tổng quan. Nếu vẫn thấy, kiểm tra quyền của bạn với phiếu đó (mục 2.10).

**Hộp ký hoàn tất báo không lấy được danh sách tiếp nhận?**
Bấm **Thử lại**. Nếu vẫn lỗi, báo quản trị. Quản trị có thể tạm tắt **Gắn phiếu với lượt tiếp nhận (HIS)** ở **Cấu hình hệ thống**. Sau khi tắt, bấm **Thử lại** là ký hoàn tất được, không cần tải lại trang.

**Không thấy phiếu mà đồng nghiệp khoa khác lập?**
Bạn chỉ thấy phiếu của khoa mình và phiếu do mình lập. Khi lập phiếu, người lập cần chọn đúng **Khoa của phiếu** (khoa điều trị người bệnh) để khoa đó thấy và xác nhận.

**Tìm người bệnh theo họ tên không ra, dù người bệnh đã có hồ sơ?**
Danh sách người bệnh chỉ có người bệnh có phiếu ở khoa của bạn, phiếu bạn lập hoặc hồ sơ bạn tiếp nhận. Người bệnh khoa khác chuyển đến: tìm bằng đúng mã người bệnh hoặc đủ 12 số CCCD (hoặc quét CCCD), rồi lập phiếu (mục 2.1).

**Tôi làm ở hai khoa, làm sao thấy phiếu của cả hai khoa?**
Nhờ quản trị thêm khoa thứ hai vào ô **Khoa/phòng khác** của tài khoản (mục 8.1), rồi đăng xuất và đăng nhập lại. Sau đó bạn thấy phiếu, báo cáo của cả hai khoa; báo cáo điều dưỡng có ô chọn khoa.

**Không thấy nút thêm khoa/phòng?**
Danh mục khoa/phòng lấy từ HIS (mục 8.4). Khoa mới phải có trên HIS trước; sau đó quản trị bấm **Lấy danh mục từ HIS**. HIS có khoa mà vẫn không lấy về được thì nhờ phòng CNTT kiểm tra câu truy vấn danh mục khoa/phòng.

**Xuất dữ liệu báo "Bạn thao tác quá nhiều lần"?**
Mỗi người được xuất tối đa 10 lần mỗi phút. Chờ một phút rồi thử lại.

**Quét CCCD (hoặc mã QR trên VNeID), địa chỉ hoặc họ tên bị mất dấu, hiện ký tự lạ?**
Máy quét 2D chưa cài chế độ đọc tiếng Việt. Phần mềm tự sửa các chữ máy quét gõ thành ký tự lạ (kiểu *Ä‘Æ°á»ng*). Máy quét bỏ hẳn chữ có dấu (kiểu *ng* thay cho *Đường*, *H Ch Minh* thay cho *Hồ Chí Minh*) thì không sửa được. Địa chỉ không sửa được thì để trống (màn hình nhân viên có cảnh báo, kiosk ghi **Chưa có** hoặc **Đã có trong hồ sơ**) để nhập lại. Họ tên bị mất chữ có dấu mà bệnh viện chưa có hồ sơ thì kiosk mời chọn **Nhập thông tin**. Thử nhanh: bật **Num Lock**, mở Notepad trên máy có máy quét và quét mã QR vào Notepad. Notepad hiện đúng dấu thì máy quét đã ổn. Khắc phục hẳn: bộ phận CNTT cài máy quét xuất ký tự Unicode (tài liệu triển khai, mục 9b), hoặc dùng camera: camera đọc đúng chữ có dấu.

**Kiosk báo đã tạm khóa tra cứu?**
Do nhập mã/CCCD kèm ngày sinh sai 5 lần. Máy mở lại sau 5 phút, hoặc quản trị **Mở khóa** tài khoản kiosk ở **Quản trị → Người dùng** (trạng thái **Tạm khóa tra cứu**). Trong lúc chờ, người bệnh chọn **Nhập thông tin** (không kèm số CCCD) hoặc đến quầy tiếp đón.

**Không lập được báo cáo điều dưỡng của khoa?**
Kiểm tra: tài khoản có vai trò Điều dưỡng và đúng khoa (quản trị sửa ở mục 8.1); khoa đã có trong danh sách khoa báo cáo (Phòng Điều dưỡng cài ở mục 9.4); ngày báo cáo không sau hôm nay và không quá một năm trước. Báo cáo đã gửi thì phải nhờ Phòng Điều dưỡng trả lại khoa mới sửa được.

**HIS lỗi, mục B hoặc mục D trên bảng tổng hợp để trống?**
Khoa được giao chưa nhập. Xem dòng **Khoa nhập tay khi HIS lỗi** ở ô trên cùng bảng tổng hợp, nhắc khoa còn ghi **chưa nhập** (mục 9.5). Nếu dòng ghi *chưa giao khoa nhập mục B, D*: Phòng Điều dưỡng giao khoa ở mục 9.4.

**Khoa tôi không thấy thẻ "Số liệu HIS khoa nhập khi HIS lỗi"?**
Thẻ chỉ có khi khoa phải gửi báo cáo, hoặc được Phòng Điều dưỡng giao nhập chỉ số mục B, D (mục 9.4). Tài khoản cần vai trò Điều dưỡng và đúng khoa (quản trị sửa ở mục 8.1).

**Phiếu mới được điền sẵn câu trả lời cũ, có cần kiểm tra không?**
Có. Đây là chức năng kế thừa phiếu lần trước (mục 2.7). Kiểm tra lại từng câu trước khi trình ký. Ý kiến Đồng ý/Không đồng ý, lời xác nhận và chữ ký không được điền sẵn.

**Không thấy menu Nội kiểm, hoặc không mở được sổ của khoa?**
Tài khoản cần quyền *Ghi kết quả nội kiểm đường huyết của khoa, gửi sổ tháng* (quản trị thêm cho vai trò ở mục 8.3, ví dụ vai trò Điều dưỡng) và đúng khoa (mục 8.1); đăng xuất rồi đăng nhập lại. Khoa chưa có máy trong danh sách sổ: nhờ Khoa HSH khai báo máy đo của khoa (mục 11.1).

**Ghi nhầm kết quả nội kiểm mà không còn nút Ghi điều chỉnh?**
Ngày đó Khoa HSH đã xem xét, hoặc khoa đã gửi sổ tháng. Báo Khoa HSH **trả lại ngày** (hoặc trả lại sổ), rồi ghi điều chỉnh có lý do (mục 11.4). Sổ đã được Khoa HSH xác nhận thì không thay đổi được nữa.
