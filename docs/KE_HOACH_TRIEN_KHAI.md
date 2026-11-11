# Kế hoạch triển khai — Quản lý Phiếu đồng thuận

> Trạng thái: **ĐÃ PHÊ DUYỆT** (11/11/2026). **Sprint 0, 1 và 2 đã hoàn thành**: xem [README](../README.md), [yêu cầu nghiệp vụ](yeu-cau-nghiep-vu.md), [kiến trúc](kien-truc.md) và [tài liệu API](api.md).
> **Cập nhật 11/11/2026:** tạm **chưa kết nối HIS và SmartCA** (quyết định số 9, mục 10); người bệnh **ký tay trên máy tính bảng** (quyết định số 10, mục 2.5a). Ba mẫu phiếu giai đoạn 1 dùng **[mẫu đề xuất](mau-phieu-de-xuat.md)** trong khi chờ bệnh viện duyệt.
> Tài liệu này gồm kế hoạch, kiến trúc, cấu trúc dự án, danh sách màn hình và lộ trình sprint.
> Mọi dữ liệu minh họa trong dự án sẽ là dữ liệu giả lập, không dùng dữ liệu người bệnh thật.

---

## 1. Phạm vi và mục tiêu

Xây dựng ứng dụng web giúp bệnh viện:

1. Lập, quản lý và lưu trữ **phiếu đồng thuận / cam kết** của người bệnh (hoặc người đại diện hợp pháp) trước khi khám bệnh, chữa bệnh (KCB). **Giai đoạn 1 (đã xác nhận) gồm 3 loại:**
   - **Giấy cam kết sử dụng dịch vụ khám** (người bệnh chọn khám dịch vụ hay khám BHYT).
   - **Phiếu cung cấp thông tin** cho người bệnh (người bệnh xác nhận đã được cung cấp thông tin).
   - **Phiếu khảo sát / đánh giá nguy cơ té ngã** (có thể có câu hỏi tính điểm, chờ mẫu của bệnh viện).

   Các phiếu đồng thuận chuyên môn (phẫu thuật, thủ thuật, gây mê, truyền máu…) để **giai đoạn sau**. Hệ thống mẫu phiếu được thiết kế chung để thêm loại mới mà không phải sửa code.
2. **Quét mã QR trên CCCD** để tự động điền thông tin nhân thân vào phiếu, **lấy thêm thông tin từ HIS FPT eHospital** (mã người bệnh, đợt khám, đối tượng BHYT/dịch vụ; *tạm chưa kết nối — quyết định 9*), sau đó nhân viên bổ sung các thông tin còn thiếu.
3. **Ký số bằng VNPT SmartCA** lên file PDF của phiếu, bảo đảm toàn vẹn và giá trị pháp lý (*tạm hoãn — quyết định 9*).
4. Lưu trữ tập trung trên **SQL Server**, hỗ trợ tra cứu và **xuất dữ liệu** (Excel/CSV/PDF) có phân quyền và ghi vết.

Ngoài phạm vi giai đoạn 1 (có thể làm sau): phiếu đồng thuận chuyên môn, ghi ngược dữ liệu/PDF vào HIS/EMR (chờ xác nhận), đăng nhập một lần (SSO/AD), ứng dụng di động riêng, chữ ký sinh trắc học.

---

## 2. Phân tích nghiệp vụ (tóm tắt)

### 2.1. Tác nhân và vai trò (RBAC)

| Vai trò | Mô tả | Quyền chính |
|---|---|---|
| Quản trị hệ thống (`Admin`) | Phòng CNTT | Quản lý người dùng, vai trò, khoa/phòng, cấu hình ký số, xem nhật ký |
| Quản lý mẫu phiếu (`TemplateManager`) | Phòng KHTH / Quản lý chất lượng | Tạo, sửa, phát hành, ngừng sử dụng mẫu phiếu theo phiên bản |
| Bác sĩ (`Doctor`) | Bác sĩ điều trị/thực hiện | Lập phiếu, giải thích, ký số SmartCA, xem phiếu của khoa mình |
| Điều dưỡng / Tiếp đón (`Nurse`) | Điều dưỡng, nhân viên tiếp đón | Quét CCCD, lập phiếu nháp, thu thập chữ ký người bệnh |
| Trưởng khoa (`DepartmentHead`) | Lãnh đạo khoa | Xem, thống kê, xuất dữ liệu trong phạm vi khoa |
| Báo cáo / Xuất dữ liệu (`Reporter`) | Phòng KHTH | Tra cứu, xuất dữ liệu toàn viện |
| Kiểm tra (`Auditor`) | Thanh tra, pháp chế, QLCL | Chỉ đọc phiếu và nhật ký audit |

Quyền sẽ được thiết kế dạng **permission** (ví dụ `consent.create`, `consent.sign`, `consent.export`, `template.publish`, `audit.read`) gán vào vai trò, kèm **phạm vi dữ liệu theo khoa**.

### 2.2. Quy trình nghiệp vụ chính

```
[Tiếp nhận người bệnh]
      │  Quét QR CCCD (camera hoặc máy quét 2D) ──► tự điền họ tên, số CCCD, ngày sinh,
      │                                               giới tính, địa chỉ, ngày cấp
      ▼
[Tìm/tạo hồ sơ người bệnh]  ── nếu đã tồn tại thì cập nhật, nếu chưa có thì tạo mới
      │  Bổ sung: SĐT, nghề nghiệp, dân tộc, mã BHYT, mã người bệnh/mã HSBA, người đại diện…
      ▼
[Chọn mẫu phiếu] (theo loại can thiệp, khoa) ──► điền nội dung chuyên môn
      ▼
[Bác sĩ giải thích] ──► ghi nhận nội dung giải thích, rủi ro, phương án thay thế
      ▼
[Người bệnh / người đại diện đồng ý hoặc từ chối]
      │  Phương án 1: Ký số VNPT SmartCA của người bệnh ──► xác nhận trên app SmartCA
      │  Phương án 2 (dự phòng): Ký tay trên máy tính bảng ──► bắt buộc chọn lý do
      │               ──► nhân viên chứng kiến ký số SmartCA xác nhận
      │  Phương án 3: Người khác ký thay bằng SmartCA của họ ──► quét CCCD người ký thay,
      │               ghi quan hệ + lý do
      ▼     ──► nhúng chữ ký vào PDF
[Bác sĩ ký số VNPT SmartCA] (chữ ký thứ 2) ──► xác nhận trên app SmartCA
      │     ──► nhúng chữ ký vào PDF, giữ nguyên chữ ký của người bệnh
      ▼
[Hoàn tất – khóa phiếu] ──► lưu PDF đã ký + băm SHA-256 ──► tra cứu, in, xuất dữ liệu
```

### 2.3. Vòng đời phiếu (state machine)

| Trạng thái | Ý nghĩa | Chuyển tiếp hợp lệ |
|---|---|---|
| `Draft` – Nháp | Đang soạn | → `PendingPatientSignature`, → `Cancelled` |
| `PendingPatientSignature` – Chờ người bệnh ký số | Đã giải thích và ghi nhận Đồng ý/Từ chối, chờ người bệnh (hoặc người đại diện) ký | → `PatientSigning` (SmartCA), → `PendingWitnessSignature` (ký tay – phương án 2), → `Draft` |
| `PatientSigning` – Người bệnh đang ký | Đã gửi yêu cầu tới SmartCA của người bệnh hoặc người ký thay | → `PendingDoctorSignature`, → `PendingPatientSignature` (hết hạn/từ chối/lỗi) |
| `PendingWitnessSignature` – Chờ nhân viên chứng kiến ký | Người bệnh đã ký tay, chờ nhân viên chứng kiến ký số SmartCA xác nhận | → `WitnessSigning`, → `PendingPatientSignature` (hủy chữ ký tay, ký lại) |
| `WitnessSigning` – Nhân viên chứng kiến đang ký | Đã gửi yêu cầu tới SmartCA của nhân viên chứng kiến | → `PendingDoctorSignature`, → `PendingWitnessSignature` (hết hạn/từ chối/lỗi) |
| `PendingDoctorSignature` – Chờ bác sĩ ký số | PDF đã có chữ ký phía người bệnh (số, hoặc tay kèm chữ ký số của người chứng kiến) | → `DoctorSigning` |
| `DoctorSigning` – Bác sĩ đang ký | Đã gửi yêu cầu tới SmartCA của bác sĩ | → `Completed`, → `PendingDoctorSignature` (hết hạn/từ chối/lỗi) |
| `Completed` – Hoàn tất | PDF có đủ 2 chữ ký số, **không được sửa**. Phiếu ghi nhận cả quyết định Đồng ý hoặc Từ chối (phiếu từ chối vẫn có giá trị pháp lý) | → `Revoked` (thu hồi, bắt buộc ghi lý do) |
| `Cancelled` / `Revoked` | Hủy nháp / thu hồi phiếu đã ký | Trạng thái cuối |

Nguyên tắc: phiếu đã ký **bất biến**. Muốn chỉnh sửa thì thu hồi phiếu cũ và lập phiếu mới có tham chiếu đến phiếu cũ.

### 2.4. Đọc QR CCCD

- Chuỗi QR trên CCCD gắn chip có dạng phân tách bằng `|`:
  `Số CCCD | Số CMND cũ | Họ và tên | Ngày sinh (ddMMyyyy) | Giới tính | Nơi thường trú | Ngày cấp (ddMMyyyy)`.
- Hỗ trợ hai cách nhập:
  1. **Camera** của máy tính bảng/điện thoại/webcam (thư viện đọc QR chạy trên trình duyệt, yêu cầu HTTPS).
  2. **Máy quét mã vạch 2D** dạng bàn phím (keyboard wedge), loại phổ biến ở quầy tiếp đón: một ô nhập nhận chuỗi rồi tự phân tích.
- Bộ phân tích (parser) chạy ở frontend để điền form ngay. Backend **kiểm tra lại** dữ liệu (định dạng 12 số, ngày hợp lệ…).
- **Không lưu chuỗi QR thô**, chỉ lưu các trường đã chuẩn hóa.
- Địa chỉ trên CCCD có thể theo địa giới hành chính cũ (trước khi sắp xếp chính quyền 2 cấp năm 2025), nên trường địa chỉ luôn **cho phép sửa**, kèm danh mục tỉnh/xã mới để chọn.

### 2.5. Ký số VNPT SmartCA và các phương án ký của người bệnh

> **Tạm hoãn** (quyết định 9): chưa tích hợp SmartCA. Thiết kế dưới đây giữ nguyên để dùng khi bật lại. Trong giai đoạn hiện tại, phiếu được **ký tay trên máy tính bảng** (mục 2.5a).

#### Nguyên tắc chung

- SmartCA là dịch vụ **ký số từ xa**: hệ thống tính băm (hash) của PDF → gửi yêu cầu ký → người ký xác nhận trên app VNPT SmartCA → hệ thống nhận chữ ký → nhúng vào PDF (chuẩn PAdES).
- Mỗi phiếu có **2 lượt ký trên cùng một file PDF**: phía người bệnh ký trước, bác sĩ ký sau. Mỗi chữ ký được thêm theo kiểu *incremental update*, nên chữ ký trước vẫn hợp lệ sau khi có chữ ký sau.
- **Người ký phía bệnh viện được cấu hình theo từng mẫu phiếu** (đã xác nhận): bác sĩ, điều dưỡng hoặc nhân viên tiếp đón. Ví dụ cam kết DV/BHYT do tiếp đón ký, khảo sát té ngã do điều dưỡng ký. **Trong tài liệu này, "bác sĩ" ở các bước ký được hiểu là "người ký phía bệnh viện" theo cấu hình của mẫu.** Người ký phía bệnh viện luôn ký số SmartCA; định danh SmartCA lưu trong hồ sơ người dùng.
- Trước mỗi lượt ký SmartCA, hệ thống tra cứu chứng thư số của người ký. Chứng thư không tồn tại, hết hạn hoặc bị thu hồi → báo rõ và gợi ý phương án tiếp theo theo thứ tự ưu tiên.
- Người ký xác nhận trên điện thoại cá nhân có cài app SmartCA. Màn hình ký hiển thị trạng thái chờ và đếm ngược.

#### Các phương án ký cho phía người bệnh

**Thứ tự ưu tiên (đã xác nhận): Phương án 1 → Phương án 3 → Phương án 2.** Giao diện hiển thị các phương án theo đúng thứ tự này. Khi phương án 1 không dùng được, hệ thống gợi ý phương án 3 trước; ký tay (phương án 2) là lựa chọn cuối cùng.

| | Phương án 1 – SmartCA của người bệnh | Phương án 2 – Ký tay trên thiết bị | Phương án 3 – SmartCA của người ký thay |
|---|---|---|---|
| Khi nào dùng | Mặc định | Người bệnh không ký SmartCA được | Người bệnh không tự ký được nhưng có người ký thay có SmartCA |
| Người ký | Người bệnh | Người bệnh (hoặc người ký thay) | Người đại diện hợp pháp / thân nhân |
| Định danh | Số CCCD người bệnh vừa quét QR (`user_id` của API SP769) | — | Số CCCD người ký thay (**quét QR CCCD của người ký thay**) |
| Bắt buộc ghi | — | Lý do + **nhân viên chứng kiến ký số SmartCA** | Lý do + quan hệ với người bệnh |
| Loại chữ ký | Chữ ký số | Ảnh chữ ký tay + chữ ký số của người chứng kiến | Chữ ký số |
| Số chữ ký số trên PDF hoàn tất | 2 (người bệnh, bác sĩ) | 2 (người chứng kiến, bác sĩ) | 2 (người ký thay, bác sĩ) |
| Bật/tắt theo khoa, loại phiếu | Luôn bật | Có | Có |

#### Phương án 2: ký tay trên thiết bị

- Nhân viên chọn phương án 2 và **bắt buộc chọn lý do** từ **danh mục lý do do quản trị cấu hình** (bệnh viện sẽ bổ sung danh sách chính thức sau; tạm seed các lý do mẫu): chưa có tài khoản SmartCA, chứng thư hết hạn/bị thu hồi, không có điện thoại, không thao tác được (cao tuổi, sức khỏe…), SmartCA lỗi/quá thời gian chờ, lý do khác (phải nhập mô tả).
- Người ký vẽ chữ ký trên màn hình cảm ứng (máy tính bảng). Hệ thống lưu ảnh chữ ký (PNG), chèn vào PDF tại ô chữ ký người bệnh, kèm dòng ghi chú "Ký tay – thời điểm – nhân viên chứng kiến".
- Ngay sau đó, **nhân viên chứng kiến ký số SmartCA** để xác nhận đã chứng kiến người bệnh ký tay. Chữ ký số này phủ lên toàn bộ PDF tại thời điểm đó, nên ảnh chữ ký tay được bảo vệ ngay từ lúc ký (sửa ảnh sẽ làm chữ ký của người chứng kiến mất hiệu lực).
- **Bất kỳ nhân viên nào** có tài khoản trong hệ thống và có SmartCA đều làm người chứng kiến được (đang đăng nhập hoặc chọn từ danh sách), **miễn là người thứ 3**: không phải bác sĩ ký phiếu và không phải người bệnh/người ký phía người bệnh (đã xác nhận). Trên PDF ghi: "Người chứng kiến: [họ tên] – [chức danh] – [khoa]".
- Nếu người chứng kiến từ chối ký hoặc hết thời gian, phiếu quay lại chờ ký; nhân viên có thể hủy chữ ký tay để người bệnh ký lại.
- Cuối cùng **bác sĩ ký số SmartCA** như các phương án khác.

#### Phương án 3: dùng SmartCA của người khác ký thay

- Áp dụng khi người bệnh **không thể tự ký**: người bệnh là trẻ em/người chưa thành niên, mất hoặc hạn chế năng lực hành vi, bất tỉnh, cấp cứu, không thao tác được, hoặc người bệnh ủy quyền cho người thân.
- Nhân viên **quét QR CCCD của người ký thay** để điền thông tin, chọn **quan hệ với người bệnh** (cha/mẹ, vợ/chồng, con, người giám hộ, người thân khác…) và **lý do ký thay** (chọn từ danh mục cấu hình được, như phương án 2). Thông tin người ký thay được lưu vào bảng `LegalRepresentatives` để dùng lại lần sau.
- Hệ thống tra cứu chứng thư SmartCA theo CCCD người ký thay và **kiểm tra chủ thể chứng thư khớp với người ký thay đã khai báo**. Không khớp → chặn ký.
- Không cho phép người ký thay là **chính bác sĩ ký phiếu** hoặc nhân viên lập phiếu (tránh xung đột lợi ích).
- Trên PDF, ô chữ ký người bệnh ghi rõ: "Ký thay cho người bệnh [họ tên] – Người ký: [họ tên] – Quan hệ: [quan hệ] – Lý do: [lý do]".
- Điều kiện ai được ký thay phải theo Luật Khám bệnh, chữa bệnh và quy định của bệnh viện. **Pháp chế bệnh viện cần xác nhận** danh sách quan hệ và lý do hợp lệ.

#### Theo dõi và an toàn

- Mọi lựa chọn phương án 2 hoặc 3 đều ghi audit (ai chọn, phương án, lý do, thời điểm). Báo cáo thống kê có tỉ lệ từng phương án theo khoa.
- Thiết kế qua interface `IDigitalSignatureService` gồm các thao tác: lấy chứng thư số, tạo yêu cầu ký, kiểm tra trạng thái giao dịch, nhúng chữ ký. Có hai cài đặt:
  - `VnptSmartCaSignatureService`: gọi **API SP769 v4.0** của VNPT (`sp_id`/`sp_password`, UAT/Production). Thiết kế chi tiết, các điểm cần xác minh và rủi ro kỹ thuật: xem [`tich-hop-smartca.md`](tich-hop-smartca.md).
  - `FakeSignatureService`: giả lập, dùng cho môi trường dev/test và CI. Không cần tài khoản thật.
- Thông tin xác thực SmartCA chỉ lấy từ **biến môi trường / secret store**, tuyệt đối không commit.
- Mọi giao dịch ký được ghi bảng `SignatureTransactions` và audit log. Hệ thống xử lý được các trường hợp hết hạn, người ký từ chối, lỗi mạng (thử lại có giới hạn).
- Sau khi ký, hệ thống kiểm tra lại chữ ký trong PDF và lưu thông tin chứng thư (serial, chủ thể, thời điểm ký).

### 2.5a. Ký tay trên máy tính bảng (đang áp dụng, quyết định 10)

Thay cho ký số SmartCA trong giai đoạn hiện tại. Khác phương án 2 ở mục 2.5: đây là cách ký chính, không cần chọn lý do và không cần người chứng kiến ký số.

- **Người ký phía người bệnh:**
  - Người bệnh tự ký; hoặc
  - Người đại diện, chọn từ danh sách người đại diện trong hồ sơ (đã quét CCCD). Phiếu ghi họ tên, quan hệ và số CCCD của người đại diện.
- **Chế độ ký:**
  - Nhân viên bấm "Trình ký", máy tính bảng chuyển sang màn hình ký toàn màn hình. Màn hình chỉ có nội dung phiếu (chỉ đọc, phóng to được) và khung ký; menu bị ẩn.
  - **Muốn thoát chế độ ký phải nhập lại mật khẩu nhân viên**, để người bệnh cầm máy không vào được phần khác của hệ thống.
- **Khung ký:**
  - Ký bằng ngón tay hoặc bút cảm ứng; có nút "Ký lại"; khung trống thì không xác nhận được.
  - Hệ thống chỉ lưu **ảnh chữ ký (PNG)**. Không lưu dữ liệu nét ký và thời gian từng nét, để không phát sinh dữ liệu sinh trắc học.
- **Phía bệnh viện:** người ký theo cấu hình mẫu xác nhận bằng tài khoản đang đăng nhập. Có ký tay thêm hay không: **chờ bệnh viện chọn** (mục 10, việc còn chờ số 1).
- **Hoàn tất:**
  - Sinh PDF có ảnh chữ ký đúng ô ký, kèm họ tên và thời điểm ký.
  - Lưu PDF cùng mã băm SHA-256; nếu file bị sửa sau khi lưu, mã băm không còn khớp.
  - Khóa nội dung phiếu và ghi nhật ký: người trình ký, người ký, thời điểm, thiết bị.
- **Thu hồi:** phiếu đã ký không sửa được. Muốn thay đổi thì thu hồi (bắt buộc lý do, có nhật ký) rồi lập phiếu mới.
- **Giới hạn:** chữ ký tay trên máy tính bảng là chữ ký điện tử thông thường, **không phải chữ ký số**.
  - Pháp chế cần xác nhận giá trị pháp lý cho 3 loại phiếu giai đoạn 1.
  - Khi tích hợp SmartCA, có thể bổ sung chữ ký số lên PDF mà không phải đổi quy trình ký tay.

### 2.6. Tích hợp HIS FPT eHospital (giai đoạn 1)

> **Tạm chưa kết nối** (quyết định 9): HIS tắt mặc định (`His:Provider=None`), giao diện ẩn thông tin HIS, mã người bệnh nhập tay. Interface `IHisGateway` giữ sẵn để bật lại.

- Bệnh viện dùng **FPT eHospital** và cần kết nối ngay giai đoạn 1 (đã xác nhận). Tài liệu API bệnh viện sẽ gửi sau.
- Thiết kế qua interface `IHisGateway` (tầng Application), gồm các thao tác dự kiến: tìm người bệnh theo số CCCD hoặc mã người bệnh; lấy đợt khám/tiếp nhận hiện tại; lấy đối tượng (BHYT/dịch vụ), số thẻ BHYT, khoa/phòng khám. Có hai cài đặt:
  - `FakeHisGateway`: dữ liệu giả lập, dùng cho dev/test/CI.
  - `FptEHospitalGateway`: viết khi có tài liệu API từ FPT.
- **Giai đoạn 1 chỉ đọc từ HIS.** Việc ghi ngược phiếu/PDF đã ký vào HIS/EMR cần xác nhận thêm.
- Khi HIS không truy cập được, nhân viên vẫn **nhập tay** được để không chặn công việc tiếp đón. Phiếu được đánh dấu "chưa đối chiếu HIS".
- Không lưu thông tin xác thực HIS trong mã nguồn; có timeout, retry giới hạn, ghi log đã che dữ liệu cá nhân.

### 2.7. Yêu cầu phi chức năng

- **Bảo mật**: HTTPS; JWT (access token ngắn hạn + refresh token xoay vòng, lưu trong cookie HttpOnly); mật khẩu băm bằng ASP.NET Core Identity; khóa tài khoản khi đăng nhập sai nhiều lần; rate limiting; CORS chặt; security headers.
- **Dữ liệu cá nhân**: số CCCD, SĐT, mã BHYT được **mã hóa ở mức cột** và **che một phần** trên giao diện (ví dụ `0790******12`). Xuất dữ liệu bắt buộc có quyền và được ghi audit. Cần đối chiếu với quy định pháp luật về bảo vệ dữ liệu cá nhân và quy định của Bộ Y tế về hồ sơ bệnh án điện tử (pháp chế bệnh viện xác nhận).
- **Audit log**: ghi ai, làm gì, lúc nào, từ IP nào, với đối tượng nào, giá trị trước/sau (đã che dữ liệu nhạy cảm). Bảng chỉ cho phép ghi thêm, không có API sửa/xóa.
- **Validation**: FluentValidation ở backend; Ant Design Form rules ở frontend. Lỗi trả về theo chuẩn `ProblemDetails` (RFC 9457) với thông báo tiếng Việt.
- **Thời gian**: lưu UTC, hiển thị theo giờ Việt Nam (UTC+7).
- **Hiệu năng (quy mô đã xác nhận: mức vừa)**: khoảng **50–200 người dùng đồng thời, 500–3.000 phiếu/ngày**. Danh sách phân trang ở server, tra cứu theo chỉ mục.
- **Dung lượng lưu trữ PDF** (ước tính, sẽ đo lại khi có mẫu thật): khoảng 200–500 KB/phiếu hoàn tất → khoảng 0,6–1,5 GB/ngày, tức 200–550 GB/năm. Chỉ giữ PDF cuối cùng; file trung gian giữa các lượt ký sẽ được dọn sau khi phiếu hoàn tất. Cần kế hoạch dung lượng và sao lưu cho máy chủ.
- **Lưu trữ (đã xác nhận)**: theo **thời hạn lưu trữ hồ sơ bệnh án** của Bộ Y tế; pháp chế bệnh viện xác nhận số năm cụ thể. Hệ thống **không tự động xóa** phiếu.
- **Triển khai (đã xác nhận)**: **máy chủ Windows Server tại bệnh viện**. Hình thức chạy (IIS + SQL Server có sẵn, hay Docker trên Windows) bệnh viện kiểm tra và trả lời sau. Docker Compose vẫn dùng cho môi trường phát triển và kiểm thử.
- **Khả năng vận hành**: log có cấu trúc, health check (`/health`), Docker Compose.

---

## 3. Kiến trúc tổng thể

### 3.1. Sơ đồ

```
┌──────────────────────────┐        HTTPS/REST (JSON)        ┌──────────────────────────────┐
│  Frontend SPA            │ ─────────────────────────────►  │  ASP.NET Core .NET 10 Web API │
│  React + TS + Ant Design │                                  │  (Clean Architecture)         │
│  Vite, React Router,     │ ◄─────────────────────────────  │                               │
│  TanStack Query          │                                  │  Api → Application → Domain   │
└──────────────────────────┘                                  │  Infrastructure / Persistence │
         ▲ camera / máy quét QR                               └──────┬───────────┬────────────┘
                                                                     │           │
                                                            EF Core  │           │ HTTPS
                                                                     ▼           ▼
                                                        ┌──────────────┐  ┌────────────────────┐
                                                        │ SQL Server   │  │ VNPT SmartCA API   │
                                                        │ (dữ liệu +   │  │ (hoặc Fake khi dev)│
                                                        │  metadata)   │  └────────────────────┘
                                                        └──────────────┘
                                                        ┌──────────────┐
                                                        │ File storage │  PDF đã ký (volume /
                                                        │              │  thư mục mạng, có thể
                                                        └──────────────┘  thay bằng object storage)
```

### 3.2. Clean Architecture – quy tắc phụ thuộc

- `Domain`: entity, value object (Cccd, PhoneNumber…), enum trạng thái, state machine, domain exception. **Không phụ thuộc** dự án nào khác.
- `Application`: use case (command/query handler tự viết, không dùng MediatR), DTO, validator, các interface (`IConsentRepository`, `IDigitalSignatureService`, `IPdfRenderer`, `IFileStorage`, `ICurrentUser`, `IAuditWriter`…). Chỉ phụ thuộc `Domain` và `Shared`.
- `Persistence`: EF Core + SQL Server, DbContext, cấu hình bảng, migration, repository, seed dữ liệu giả.
- `Infrastructure`: SmartCA client, tạo PDF, lưu file, xuất Excel, mã hóa, đồng hồ hệ thống.
- `Api`: controller, middleware (xử lý lỗi, correlation id, audit), cấu hình xác thực/phân quyền, OpenAPI, DI. **Không chứa nghiệp vụ.**
- `Shared`: kiểu dùng chung (Result, PagedResult, mã lỗi).

### 3.3. Thư viện đề xuất (tối thiểu, ưu tiên mã nguồn mở có giấy phép thân thiện)

| Mục đích | Backend | Frontend |
|---|---|---|
| Nền tảng | ASP.NET Core .NET 10, EF Core 10 (SQL Server) | React 19, TypeScript, Vite |
| UI | — | Ant Design (locale `vi_VN`), dayjs |
| Validation | FluentValidation | Ant Design Form rules |
| Xác thực | ASP.NET Core Identity + JWT Bearer | — |
| Gọi API | — | TanStack Query + fetch wrapper |
| Định tuyến | — | React Router |
| Tài liệu API | OpenAPI (`Microsoft.AspNetCore.OpenApi`) + Scalar/Swagger UI | — |
| Log | Serilog | — |
| PDF | PDFsharp (MIT) để tạo PDF, BouncyCastle (MIT) để dựng chữ ký CMS/PAdES (ký 2 pha); QuestPDF chỉ dùng nếu bệnh viện đáp ứng điều kiện license | Xem PDF bằng trình xem có sẵn của trình duyệt |
| Excel | ClosedXML (MIT) | — |
| QR | — | `@zxing/browser` (đọc QR bằng camera) |
| Chữ ký tay (phương án 2) | — | `signature_pad` (vẽ chữ ký trên màn hình cảm ứng) |
| Test | xUnit, NSubstitute, Testcontainers (SQL Server) | Vitest, Testing Library, MSW; Playwright cho E2E (tùy chọn) |
| Lint | Roslyn analyzers, warnings-as-errors | oxlint |

> Tránh MediatR và AutoMapper vì các thư viện này đã chuyển sang giấy phép thương mại từ năm 2025. Handler và mapping sẽ được viết tay.

---

## 4. Thiết kế cơ sở dữ liệu (SQL Server – mức khái niệm)

Quy ước: khóa chính `UNIQUEIDENTIFIER` (UUID), mã nghiệp vụ là cột riêng (ví dụ `PDT-2026-000001`), thời gian `datetime2` UTC, có cột `RowVersion` cho optimistic concurrency, xóa mềm (`IsDeleted`) với danh mục.

| Bảng | Mô tả | Cột chính |
|---|---|---|
| `Departments` | Khoa/phòng | Id, Code, Name, IsActive |
| `Users` (Identity) | Tài khoản nhân viên | Id, UserName, FullName, Title, DepartmentId, SmartCaUserId (định danh SmartCA, không phải bí mật), IsActive |
| `Roles`, `UserRoles` | Vai trò (Identity) | |
| `Permissions`, `RolePermissions` | Quyền chi tiết | Code, Description |
| `Patients` | Người bệnh | Id, PatientCode, FullName, DateOfBirth, Gender, **CitizenIdEncrypted**, **CitizenIdHash** (tra cứu), OldIdNumber, IdIssueDate, PermanentAddress, CurrentAddress, Phone (mã hóa), Ethnicity, Occupation, HealthInsuranceNo (mã hóa) |
| `LegalRepresentatives` | Người đại diện/thân nhân/người ký thay | Id, PatientId, FullName, DateOfBirth, Relationship, CitizenIdEncrypted, CitizenIdHash, Phone |
| `ConsentTemplates` | Loại mẫu phiếu | Id, Code, Name, Category (Cam kết DV/BHYT, Cung cấp thông tin, Khảo sát té ngã; giai đoạn sau: Phẫu thuật/Thủ thuật/Gây mê/Truyền máu…), HospitalSignerRole (vai trò ký phía bệnh viện), DepartmentId (null = toàn viện) |
| `ConsentTemplateVersions` | Phiên bản mẫu | Id, TemplateId, VersionNo, ContentSchemaJson (định nghĩa trường), LayoutHtml/Body, Status (Draft/Published/Retired), PublishedAt |
| `ConsentForms` | Phiếu đồng thuận | Id, FormNo, PatientId, RepresentativeId?, TemplateVersionId, DepartmentId, EncounterCode (mã đợt KCB/HSBA), ProcedureName, PlannedDate, DoctorId, Status, Decision (Đồng ý/Từ chối), DataJson (nội dung điền theo schema), CreatedBy, CreatedAt, CompletedAt, RevokedReason, ReplacesFormId? |
| `ConsentSignatures` | Chữ ký trên phiếu | Id, FormId, SignerType (Patient/Representative/Doctor/Witness), SignerName, Method (SmartCA/Handwritten/ProxySmartCA), OnBehalfOfPatient (bool), RepresentativeId (khi ký thay), Relationship (khi ký thay), FallbackReason (khi ký tay/ký thay), WitnessUserId (khi ký tay; người chứng kiến cũng có một dòng chữ ký riêng với SignerType = Witness), SignatureImagePath (khi ký tay), SignOrder, SignedAt, CertificateSerial, CertificateSubject, CertificateValidTo, SignatureTransactionId |
| `SignatureTransactions` | Giao dịch SmartCA | Id, FormId, SignerType, SignerSmartCaId (đã mã hóa), ProviderTransactionId, Status, RequestedAt, ExpiresAt, CompletedAt, ErrorCode |
| `StoredDocuments` | File PDF | Id, FormId, Kind (Unsigned/Signed), StoragePath, Sha256, SizeBytes, CreatedAt |
| `SigningReasons` | Danh mục lý do ký tay/ký thay (cấu hình được) | Id, Code, Name, AppliesTo (Handwritten/Proxy), RequiresNote, SortOrder, IsActive |
| `ExportJobs` | Lịch sử xuất dữ liệu | Id, RequestedBy, FilterJson, Format, RowCount, CreatedAt |
| `AuditLogs` | Nhật ký (chỉ ghi thêm) | Id, OccurredAt, UserId, UserName, Action, EntityType, EntityId, IpAddress, UserAgent, CorrelationId, ChangesJson (đã che dữ liệu nhạy cảm) |
| `RefreshTokens` | Token làm mới | Id, UserId, TokenHash, ExpiresAt, RevokedAt, ReplacedBy |

Chỉ mục chính: `Patients(CitizenIdHash)` unique có lọc, `Patients(PatientCode)`, `ConsentForms(FormNo)` unique, `ConsentForms(Status, DepartmentId, CreatedAt)`, `AuditLogs(OccurredAt)`, `AuditLogs(EntityType, EntityId)`.

Xuất dữ liệu: dùng view `vw_ConsentFormExport` (đã làm phẳng) cho Excel/CSV. Số CCCD **mặc định bị che**, chỉ người có quyền `consent.export.sensitive` mới xuất được bản đầy đủ.

---

## 5. Thiết kế module và API REST (phiên bản `v1`)

| Module | Endpoint chính |
|---|---|
| Auth | `POST /api/v1/auth/login`, `POST /auth/refresh`, `POST /auth/logout`, `GET /auth/me`, `POST /auth/change-password` |
| Users & Roles | `GET/POST /users`, `GET/PUT /users/{id}`, `POST /users/{id}/lock`, `GET /roles`, `PUT /roles/{id}/permissions` |
| Departments | `GET/POST /departments`, `PUT/DELETE /departments/{id}` |
| Patients | `GET /patients?keyword=`, `POST /patients`, `GET/PUT /patients/{id}`, `POST /patients/parse-cccd-qr` (kiểm tra + tìm hồ sơ trùng), `GET /patients/{id}/consents` |
| Templates | `GET/POST /consent-templates`, `POST /consent-templates/{id}/versions`, `POST /consent-templates/{id}/versions/{v}/publish`, `.../retire` |
| Consent Forms | `GET /consent-forms?status=&department=&from=&to=&page=`, `POST /consent-forms`, `GET/PUT /consent-forms/{id}`, `POST /{id}/submit`, `POST /{id}/decision` (Đồng ý/Từ chối), `POST /{id}/revoke`, `GET /{id}/pdf`, `GET /{id}/history` |
| Signing | `GET /consent-forms/{id}/signer-certificate?signer=patient\|doctor` (tra cứu chứng thư), `POST /consent-forms/{id}/sign/patient`, `POST /consent-forms/{id}/sign/doctor` (tạo yêu cầu SmartCA), `POST /consent-forms/{id}/sign/patient-handwritten` (phương án 2: ảnh chữ ký + lý do), `POST /consent-forms/{id}/sign/proxy` (phương án 3: người ký thay + quan hệ + lý do), `POST /consent-forms/{id}/sign/witness` (người chứng kiến ký SmartCA sau khi người bệnh ký tay), `GET /signing-transactions/{txId}` (trạng thái), `POST /signing-transactions/{txId}/cancel` |
| Export & Reports | `POST /exports/consent-forms` (Excel/CSV), `GET /reports/summary?from=&to=` |
| Audit | `GET /audit-logs?user=&action=&entity=&from=&to=` |
| Hệ thống | `GET /health`, `GET /openapi/v1.json` |

Mỗi endpoint đều có: `[Authorize(Policy = "...")]` theo permission, validation, lỗi trả về dạng `ProblemDetails`, `CancellationToken`, phân trang chuẩn `{ items, page, pageSize, totalCount }`.

---

## 6. Cấu trúc dự án đề xuất

> Lưu ý: `copilot-instructions.md` hiện ghi PostgreSQL, Material UI, .NET 9. Với dự án này, tôi sẽ **làm theo công nghệ bắt buộc anh/chị yêu cầu** (SQL Server, Ant Design, .NET 10) và cập nhật lại file hướng dẫn đó ở Sprint 0. Các quy tắc Clean Architecture và bảo mật vẫn giữ nguyên.

```
bth-bekp/
├── backend/
│   ├── ConsentManagement.slnx
│   ├── Directory.Build.props            # nullable, warnings-as-errors, analyzers
│   ├── src/
│   │   ├── Consent.Domain/              # Entities, ValueObjects (Cccd...), Enums, StateMachine
│   │   ├── Consent.Application/         # UseCases (Commands/Queries), DTOs, Validators, Abstractions
│   │   ├── Consent.Persistence/         # DbContext, Configurations, Migrations, Repositories, Seed (dữ liệu giả)
│   │   ├── Consent.Infrastructure/      # SmartCA (Vnpt + Fake), Pdf, FileStorage, Excel, Encryption
│   │   ├── Consent.Shared/              # Result, Errors, Paging
│   │   └── Consent.Api/                 # Controllers, Middleware, Auth, OpenAPI, Program.cs
│   └── tests/
│       ├── Consent.Domain.Tests/
│       ├── Consent.Application.Tests/
│       ├── Consent.Api.IntegrationTests/        # WebApplicationFactory + Testcontainers SQL Server
│       └── Consent.ArchitectureTests/           # kiểm tra quy tắc phụ thuộc giữa các tầng
├── frontend/
│   ├── package.json, vite.config.ts, tsconfig.json, .oxlintrc.json
│   └── src/
│       ├── app/                 # providers, router, theme Ant Design, ConfigProvider vi_VN
│       ├── api/                 # http client, kiểu dữ liệu, hooks TanStack Query
│       ├── auth/                # đăng nhập, guard theo quyền, usePermission
│       ├── components/          # layout, PageHeader, StatusTag, MaskedText, QrScanner, SmartCaSignDialog, HandwrittenSignaturePad
│       ├── features/
│       │   ├── dashboard/
│       │   ├── patients/        # gồm cccdQrParser.ts + test
│       │   ├── consent-forms/
│       │   ├── templates/
│       │   ├── signing/
│       │   ├── exports/
│       │   ├── audit/
│       │   └── admin/           # users, roles, departments
│       ├── utils/               # format ngày, che dữ liệu
│       └── test/
├── database/
│   └── scripts/                 # script xuất dữ liệu, view, ghi chú backup (migration chính nằm ở EF)
├── docs/
│   ├── KE_HOACH_TRIEN_KHAI.md   # tài liệu này
│   ├── yeu-cau-nghiep-vu.md     # SRS chi tiết (Sprint 0)
│   ├── kien-truc.md             # kiến trúc + ERD (Mermaid)
│   ├── api.md                   # hướng dẫn dùng API (kèm OpenAPI tự sinh)
│   ├── tich-hop-smartca.md
│   └── trien-khai.md            # Docker, cấu hình, bảo mật, backup
├── scripts/                     # dev-up.sh, run-tests.sh
├── docker-compose.yml           # sqlserver + api + web (nginx)
├── docker-compose.override.yml  # cấu hình dev
├── .env.example                 # chỉ có giá trị mẫu, không có bí mật
├── .github/workflows/ci.yml     # build + lint + test
└── README.md
```

---

## 7. Danh sách màn hình (tiếng Việt, responsive)

Bố cục chung: Ant Design `Layout` có thanh menu bên trái (thu gọn thành drawer trên điện thoại/máy tính bảng), header hiển thị người dùng, khoa và nút đăng xuất. Màu chủ đạo xanh y tế, font hỗ trợ tiếng Việt, locale `vi_VN`. Tối ưu cho **máy tính bàn ở quầy tiếp đón** và **máy tính bảng tại giường bệnh** (để người bệnh ký).

| # | Màn hình | Đường dẫn | Vai trò | Nội dung chính |
|---|---|---|---|---|
| 1 | Đăng nhập | `/dang-nhap` | Tất cả | Tên đăng nhập, mật khẩu, thông báo khóa tài khoản |
| 2 | Tổng quan (Dashboard) | `/` | Tất cả | Số phiếu theo trạng thái, phiếu chờ ký của tôi, biểu đồ theo khoa/ngày |
| 3 | Danh sách phiếu đồng thuận | `/phieu-dong-thuan` | Bác sĩ, ĐD, TK | Bảng có lọc (trạng thái, khoa, loại phiếu, ngày, từ khóa), thẻ trạng thái màu, thao tác nhanh |
| 4 | Lập phiếu mới – Bước 1: Người bệnh | `/phieu-dong-thuan/tao-moi` | Bác sĩ, ĐD | **Nút "Quét QR CCCD"** (camera hoặc máy quét), tự điền, cảnh báo nếu đã có hồ sơ trùng, bổ sung thông tin thiếu, người đại diện |
| 5 | Lập phiếu – Bước 2: Chọn mẫu và nội dung | (wizard) | Bác sĩ | Chọn mẫu theo khoa/loại; form động theo schema của mẫu: chẩn đoán, phương pháp, rủi ro, phương án thay thế |
| 6 | Lập phiếu – Bước 3: Xem trước và người bệnh ký số | (wizard) | Bác sĩ, ĐD | Xem trước PDF, chọn Đồng ý/Từ chối, chọn người ký (người bệnh/người đại diện), kiểm tra chứng thư SmartCA, gửi yêu cầu ký; chọn phương án ký theo thứ tự ưu tiên: **1. SmartCA người bệnh** → **3. SmartCA người ký thay** (quét CCCD người ký thay, quan hệ, lý do) → **2. Ký tay** (khung ký toàn màn hình + lý do + chọn nhân viên chứng kiến, sau đó gửi yêu cầu ký SmartCA cho người chứng kiến) |
| 7 | Ký số SmartCA | hộp thoại | Người bệnh, Bác sĩ | Dùng chung cho cả 2 lượt ký: hiển thị chứng thư, gửi yêu cầu, đếm ngược "Vui lòng xác nhận trên ứng dụng VNPT SmartCA", kết quả thành công/thất bại |
| 8 | Chi tiết phiếu | `/phieu-dong-thuan/:id` | Theo quyền | Thông tin, PDF đã ký, danh sách chữ ký, lịch sử trạng thái, nút In/Tải/Thu hồi |
| 9 | Danh sách người bệnh | `/nguoi-benh` | Bác sĩ, ĐD | Tìm theo tên, mã người bệnh, CCCD (đã che) |
| 10 | Hồ sơ người bệnh | `/nguoi-benh/:id` | Bác sĩ, ĐD | Thông tin nhân thân, người đại diện, các phiếu đã ký |
| 11 | Quản lý mẫu phiếu | `/mau-phieu` | QL mẫu | Danh sách mẫu, phiên bản, trạng thái phát hành |
| 12 | Soạn mẫu phiếu | `/mau-phieu/:id/phien-ban/:v` | QL mẫu | Định nghĩa trường (text, ngày, chọn, checkbox), nội dung cam kết, xem trước |
| 13 | Xuất dữ liệu | `/xuat-du-lieu` | Reporter, TK | Bộ lọc, chọn cột, định dạng Excel/CSV, lịch sử xuất |
| 14 | Báo cáo thống kê | `/bao-cao` | Reporter, TK | Theo khoa, loại phiếu, tỉ lệ từ chối, tỉ lệ từng phương án ký (SmartCA / ký tay / ký thay), thời gian hoàn tất |
| 15 | Nhật ký hệ thống (Audit) | `/nhat-ky` | Admin, Auditor | Lọc theo người dùng, hành động, đối tượng, thời gian; xem chi tiết thay đổi |
| 16 | Người dùng | `/quan-tri/nguoi-dung` | Admin | Tạo/sửa/khóa, gán vai trò, khoa, mã SmartCA |
| 17 | Vai trò và quyền | `/quan-tri/vai-tro` | Admin | Ma trận quyền theo vai trò |
| 18 | Khoa/phòng | `/quan-tri/khoa-phong` | Admin | Danh mục khoa |
| 19 | Cấu hình hệ thống | `/quan-tri/cau-hinh` | Admin | Môi trường SmartCA (sandbox/production), thời hạn giao dịch ký, bật/tắt phương án 2/3 theo khoa/loại phiếu, **danh mục lý do ký tay/ký thay**. Không hiển thị secret. |
| 20 | Hồ sơ cá nhân / Đổi mật khẩu | `/tai-khoan` | Tất cả | Đổi mật khẩu, xem vai trò |
| 21 | Trang lỗi 403 / 404 / 500 | — | — | Thông báo thân thiện bằng tiếng Việt |

---

## 8. Kế hoạch sprint (mỗi sprint khoảng 1–2 tuần, mỗi sprint là 1 hoặc vài PR nhỏ)

### Sprint 0 – Nền móng và tài liệu yêu cầu
- SRS chi tiết (`docs/yeu-cau-nghiep-vu.md`), ERD, quyết định kiến trúc.
- Khung solution backend (6 project + test projects), khung frontend (Vite + Ant Design + Router + Query).
- Docker Compose: SQL Server + API + Web; `.env.example`; CI GitHub Actions; cập nhật `copilot-instructions.md`.

**Tiêu chí nghiệm thu**
- `docker compose up` chạy được; `GET /health` trả 200; trang web hiển thị layout tiếng Việt.
- `dotnet build` (warnings-as-errors), `dotnet test`, `npm run lint`, `npm test`, `npm run build` đều đạt; CI xanh.
- Test kiến trúc chứng minh Domain không phụ thuộc tầng khác.
- Repo không chứa bí mật hay dữ liệu thật.

### Sprint 1 – Xác thực, RBAC, audit log, xử lý lỗi
- Identity + JWT + refresh token (cookie HttpOnly), khóa tài khoản, đổi mật khẩu.
- Permission-based policies, phạm vi theo khoa; seed vai trò và tài khoản demo (mật khẩu lấy từ biến môi trường).
- Middleware lỗi → `ProblemDetails` tiếng Việt; correlation id; audit log tự động cho thao tác ghi và đăng nhập.
- Frontend: màn hình 1, 20, 21, guard theo quyền, quản lý người dùng/vai trò/khoa (16, 17, 18).

**Điều chỉnh khi triển khai (đã ghi vào ADR-009…013 trong [kiến trúc](kien-truc.md))**
- Dùng mô hình người dùng/vai trò riêng trong Domain, chỉ dùng thuật toán băm mật khẩu của ASP.NET Core Identity (không dùng các bảng Identity).
- Danh mục quyền định nghĩa trong mã nguồn; gán cho vai trò qua bảng `RolePermissions` (không cần bảng `Permissions`).
- Thêm vai trò **Tiếp đón** (`RECEPTIONIST`) do phiếu cam kết DV/BHYT do tiếp đón ký.
- Tìm kiếm tên không dấu dùng collation `Latin1_General_100_CI_AI`.

**Tiêu chí nghiệm thu** (✅ đã đạt, có test tự động)
- Truy cập không có token → 401; thiếu quyền → 403 (có integration test).
- Đăng nhập sai 5 lần → khóa tạm thời; refresh token dùng lại sau khi đã xoay vòng → bị từ chối.
- Mọi thao tác tạo/sửa/xóa và đăng nhập đều có bản ghi audit; không có API sửa/xóa audit.
- Menu và nút ẩn/hiện đúng theo quyền.

### Sprint 2 – Người bệnh và quét QR CCCD
- Parser QR CCCD (frontend) và validator (backend) với bộ test gồm chuỗi hợp lệ, thiếu trường, sai ngày, sai số chữ số, ký tự lạ.
- Component `QrScanner` (camera) và ô nhận dữ liệu từ máy quét 2D.
- CRUD người bệnh, phát hiện trùng theo hash CCCD, mã hóa CCCD/SĐT/BHYT, che dữ liệu trên UI.
- `IHisGateway` + `FakeHisGateway`; màn hình 4 tra cứu HIS sau khi quét CCCD. `FptEHospitalGateway` làm ngay khi có tài liệu API (có thể tách thành PR riêng).
- Màn hình 4, 9, 10.

**Điều chỉnh khi triển khai (ADR-014…016 trong [kiến trúc](kien-truc.md))**
- Mã hóa cột ở tầng ứng dụng (AES-256-GCM) + chỉ mục mù HMAC để tra cứu/chặn trùng CCCD; thêm mã hóa số CMND cũ.
- Đọc QR ở trình duyệt, máy chủ kiểm tra lại từng trường (gồm kiểm tra số CCCD khớp năm sinh/giới tính); không có API nhận chuỗi QR gốc.
- Trạng thái "đã/chưa đối chiếu HIS" được lưu ở hồ sơ người bệnh (máy chủ tự đối chiếu khi lưu); từ Sprint 3 sẽ gắn thêm vào từng phiếu.
- Mỗi lần xem hồ sơ người bệnh được ghi nhật ký; số CCCD không đi trong URL.
- Kết nối FPT eHospital thật chưa làm vì chưa có tài liệu API (đang dùng HIS giả lập qua cùng interface).
- Sau quyết định 9 (chưa kết nối HIS): HIS **tắt mặc định**, giao diện ẩn mọi thông tin HIS theo `GET /api/v1/features`; HIS giả lập chỉ dùng khi phát triển/kiểm thử và **bị chặn ở Production** (ADR-017).
- Công cụ build frontend nâng lên Node.js 24 LTS (từ 24.15): thư viện quét QR bằng camera (`@zxing/browser`) yêu cầu Node ≥ 24, và Node 22 hết hỗ trợ tháng 4/2027. Chỉ ảnh hưởng máy phát triển, CI và bước build Docker; máy chủ chỉ phục vụ file tĩnh.
- Thay các API Ant Design đã lỗi thời (`Alert.message` → `title`, `Space.direction` → `orientation`, `List` → `Menu` ở trang Vai trò, chọn được bằng bàn phím). Test frontend tự trượt khi Ant Design cảnh báo, vì Vitest ẩn console của test đạt.

**Tiêu chí nghiệm thu** (✅ đã đạt, có test tự động)
- Quét chuỗi QR mẫu (giả lập) → điền đúng 7 trường, ngày định dạng `dd/MM/yyyy`.
- Quét CCCD đã có hồ sơ → hiển thị hồ sơ cũ và hỏi cập nhật, không tạo bản trùng.
- CCCD trong DB được lưu ở dạng mã hóa (có test kiểm tra); API danh sách trả về CCCD đã che.
- Hoạt động trên Chrome máy tính bàn và máy tính bảng (camera qua HTTPS).
- Quét CCCD → tự lấy mã người bệnh, đợt khám, đối tượng BHYT/dịch vụ từ HIS (giả lập, khi bật HIS); HIS lỗi/timeout → vẫn nhập tay được và phiếu được đánh dấu "chưa đối chiếu HIS" (có test). Mặc định chưa kết nối HIS: không hiển thị thông tin HIS (có test).

### Sprint 3 – Mẫu phiếu và lập phiếu đồng thuận
- Quản lý mẫu phiếu có phiên bản, schema trường động; dựng **3 mẫu giai đoạn 1** (cam kết DV/BHYT, cung cấp thông tin, đánh giá nguy cơ té ngã) theo **[mẫu đề xuất](mau-phieu-de-xuat.md)** (bệnh viện duyệt/chỉnh trên đó; khi có mẫu giấy chính thức thì cập nhật nội dung, không sửa code). Schema hỗ trợ câu hỏi chọn/điểm cho phiếu đánh giá.
- Wizard lập phiếu (màn hình 5), state machine, danh sách và chi tiết phiếu (3, 8, 11, 12).
- Sinh PDF từ mẫu + dữ liệu, có font tiếng Việt.

**Tiêu chí nghiệm thu**
- Chỉ mẫu ở trạng thái `Published` mới được chọn; phiếu giữ nguyên phiên bản mẫu tại thời điểm lập.
- Chuyển trạng thái sai quy tắc → 409/422 kèm thông báo rõ (unit test đầy đủ cho state machine).
- PDF hiển thị đúng tiếng Việt có dấu, đủ thông tin người bệnh và nội dung.
- Danh sách lọc/phân trang ở server; người dùng chỉ thấy phiếu trong phạm vi khoa (trừ vai trò toàn viện).

### Sprint 4 – Ký tay trên máy tính bảng (quyết định 10)
- **Màn hình 6:** ghi nhận Đồng ý/Từ chối; chọn người ký phía người bệnh (người bệnh, hoặc người đại diện trong hồ sơ).
- **Chế độ ký:** toàn màn hình trên máy tính bảng; thoát phải nhập lại mật khẩu nhân viên.
- **Khung ký:** canvas dùng Pointer Events (ngón tay, bút cảm ứng, chuột); có "Ký lại", chặn khung trống. Lưu ảnh PNG, không lưu dữ liệu nét.
- **Phía bệnh viện:** xác nhận bằng tài khoản; ký tay thêm nếu bệnh viện chọn.
- **Hoàn tất:** chèn ảnh chữ ký vào PDF (Sprint 3 đã sinh PDF); lưu PDF và SHA-256; khóa phiếu; ghi nhật ký đầy đủ; thu hồi phải có lý do.

**Tiêu chí nghiệm thu**
- **Dữ liệu chữ ký:**
  - Khung ký trống → không xác nhận được (test giao diện + API 422).
  - Ảnh chữ ký không phải PNG hợp lệ hoặc quá lớn → API từ chối.
  - Không lưu dữ liệu nét ký. Chỉ người có quyền xem phiếu mới xem được ảnh chữ ký.
- **Chế độ ký:** chưa nhập đúng mật khẩu nhân viên thì không thoát được (test giao diện + API).
- **PDF:**
  - Khi người đại diện ký: PDF ghi đúng họ tên, quan hệ, số CCCD của người đại diện (có test).
  - PDF hoàn tất có ảnh chữ ký đúng ô ký, kèm họ tên và thời điểm ký.
  - SHA-256 lưu trong CSDL khớp file (integration test).
- **Sau khi ký:** phiếu không sửa được (test API); thu hồi bắt buộc lý do và có audit.
- **Thiết bị:** ký được trên máy tính bảng (Safari iPad, Chrome Android) bằng ngón tay và bút. Kiểm bằng E2E giả lập cảm ứng, và thử trên máy thật khi bệnh viện có máy.

### Giai đoạn sau – Ký số VNPT SmartCA cho người bệnh và bác sĩ (TẠM HOÃN)

> Quyết định 9 (11/11/2026): chưa tích hợp SmartCA. Nội dung dưới đây giữ làm thiết kế khi bật lại. Giai đoạn hiện tại dùng ký tay trên máy tính bảng (Sprint 4).

- Ghi nhận Đồng ý/Từ chối, chọn người ký (người bệnh/người đại diện) (màn hình 6).
- Phương án 2: ký tay trên thiết bị cảm ứng cho người bệnh, bắt buộc lý do, có cấu hình bật/tắt.
- Phương án 3: người khác ký thay bằng SmartCA của họ (quét CCCD người ký thay, quan hệ, lý do, kiểm tra chủ thể chứng thư), có cấu hình bật/tắt.
- `IDigitalSignatureService` gồm `FakeSignatureService` và `VnptSmartCaSignatureService` (cấu hình qua biến môi trường); tra cứu chứng thư, luồng ký bất đồng bộ + kiểm tra trạng thái giao dịch (màn hình 7).
- Ký 2 lượt trên cùng PDF: người bệnh trước, bác sĩ sau (PAdES, incremental update). Lưu PDF sau mỗi lượt ký và SHA-256, khóa phiếu khi đủ chữ ký.
- Hoàn thiện `docs/tich-hop-smartca.md` theo kết quả spike.
- **Rủi ro:** ký PDF 2 pha với SmartCA (chỉ hỗ trợ ký hash). Đề xuất làm spike kỹ thuật trên UAT **song song Sprint 1–2**, ngay khi có `sp_id`/`sp_password` UAT.

**Tiêu chí nghiệm thu**
- Với Fake provider: luồng ký thành công, hết hạn, bị từ chối, người bệnh chưa có SmartCA đều có test tự động và hiển thị đúng trên UI, cho cả lượt ký của người bệnh và bác sĩ.
- Bác sĩ không thể ký trước khi người bệnh ký xong (test state machine và API).
- Ký tay không có lý do → 422; bác sĩ không thể dùng phương án ký tay (test API); khi phương án 2 bị tắt cho khoa/loại phiếu → nút bị ẩn và API trả 403/422.
- Ký tay: bác sĩ không thể ký khi người chứng kiến chưa ký số; người chứng kiến trùng bác sĩ ký phiếu hoặc trùng người bệnh (cùng số CCCD) → bị chặn (có test).
- PDF ký tay hoàn tất: ảnh chữ ký hiển thị đúng vị trí, có 2 chữ ký số hợp lệ (người chứng kiến, bác sĩ).
- Ký thay: thiếu quan hệ hoặc lý do → 422; chủ thể chứng thư không khớp người ký thay → bị chặn; người ký thay trùng bác sĩ/nhân viên lập phiếu → bị chặn (có test). PDF ghi rõ "Ký thay cho người bệnh…" và có 2 chữ ký số hợp lệ.
- PDF hoàn tất mở bằng Adobe Reader hiển thị **2 chữ ký hợp lệ**, chữ ký của người bệnh không bị vô hiệu sau khi bác sĩ ký (khi dùng SmartCA sandbox; cần VNPT cấp tài khoản thử).
- Phiếu `Completed` không sửa được (test API); thu hồi bắt buộc nhập lý do và có audit.
- Không có client secret nào trong mã nguồn hoặc log.

### Sprint 5 – Xuất dữ liệu, báo cáo, dashboard
- Xuất Excel/CSV theo bộ lọc, chọn cột, che dữ liệu mặc định; lịch sử xuất (màn hình 13).
- Báo cáo thống kê và dashboard (2, 14); màn hình nhật ký audit (15).

**Tiêu chí nghiệm thu**
- File Excel mở được, tiêu đề tiếng Việt, số liệu khớp với bộ lọc (integration test).
- Người không có quyền `consent.export` → 403; mỗi lần xuất có bản ghi `ExportJobs` và audit.
- Xuất 10.000 phiếu giả lập dưới 30 giây trên môi trường Docker tham chiếu.

### Sprint 6 – Hoàn thiện, bảo mật, tài liệu triển khai
- Rà soát bảo mật (OWASP ASVS cơ bản), security headers, rate limiting, kiểm tra dependency.
- README, `docs/api.md`, `docs/trien-khai.md` (Docker, HTTPS, backup/restore SQL Server, xoay vòng khóa mã hóa).
- E2E Playwright cho luồng chính: đăng nhập → quét QR → lập phiếu → ký → xuất.
- Kiểm thử giao diện responsive (điện thoại, máy tính bảng, máy tính bàn).

**Tiêu chí nghiệm thu**
- Toàn bộ test đạt; độ bao phủ Domain + Application ≥ 80%.
- Triển khai mới hoàn toàn theo `docs/trien-khai.md` thành công trên máy sạch.
- Không còn lỗ hổng mức High/Critical trong `dotnet list package --vulnerable` và `npm audit`.

---

## 9. Chiến lược kiểm thử

| Mức | Công cụ | Phạm vi |
|---|---|---|
| Unit – Domain | xUnit | Value object (Cccd, ngày), state machine phiếu, quy tắc nghiệp vụ |
| Unit – Application | xUnit + NSubstitute | Handler, validator, phân quyền theo khoa, luồng ký với provider giả |
| Integration – API | WebApplicationFactory + Testcontainers SQL Server | Auth 401/403, CRUD, chuyển trạng thái, xuất dữ liệu, audit |
| Architecture | NetArchTest (hoặc kiểm tra reflection tự viết) | Quy tắc phụ thuộc giữa các tầng |
| Unit – Frontend | Vitest + Testing Library + MSW | Parser QR, che dữ liệu, form validation, guard quyền |
| E2E | Playwright | Luồng chính end-to-end |

Toàn bộ dữ liệu test được sinh giả lập (tên, số CCCD ngẫu nhiên không thuộc người thật, địa chỉ giả).

---

## 10. Quyết định đã chốt và việc còn chờ

**Đã xác nhận**

| # | Chủ đề | Quyết định |
|---|---|---|
| 1 | Ký số (*tạm hoãn, xem #9*) | Người bệnh và phía bệnh viện đều ký **VNPT SmartCA** (API SP769, xem [`tich-hop-smartca.md`](tich-hop-smartca.md)). Hợp đồng cho phép người bệnh dùng SmartCA cá nhân; **lượt ký trừ vào gói của người bệnh**, hết lượt thì gợi ý PA3 rồi PA2 |
| 1a | Phương án ký phía người bệnh | **PA1 → PA3 → PA2**. PA2 (ký tay) bắt buộc nhân viên chứng kiến ký SmartCA; người chứng kiến là **bất kỳ nhân viên nào, miễn là người thứ 3**. Danh sách lý do ký tay/ký thay **bệnh viện bổ sung sau** (danh mục cấu hình được) |
| 2 | Loại phiếu giai đoạn 1 | Cam kết khám DV/BHYT, Phiếu cung cấp thông tin, Đánh giá nguy cơ té ngã. Dùng **[mẫu đề xuất](mau-phieu-de-xuat.md)** (chờ bệnh viện duyệt). **Phiếu cung cấp thông tin: bệnh viện cung cấp, người bệnh xác nhận** (đã xác nhận 11/11/2026) |
| 3 | HIS | **FPT eHospital**, chỉ đọc. **Tạm chưa kết nối** (xem #9) |
| 4 | Quy mô | Mức vừa: 50–200 người dùng đồng thời, 500–3.000 phiếu/ngày |
| 5 | Triển khai | Máy chủ **Windows Server** tại bệnh viện |
| 6 | Lưu trữ | Theo thời hạn lưu trữ **hồ sơ bệnh án**, không tự động xóa |
| 7 | Repo | Repo `bth-bekp` này, module `Consent.*` |
| 8 | Người ký phía bệnh viện | **Cấu hình theo từng mẫu phiếu** (bác sĩ / điều dưỡng / tiếp đón); người chứng kiến (PA2) phải khác người ký phía bệnh viện |
| 9 | Tích hợp (11/11/2026) | **Chưa cần kết nối HIS và SmartCA ở thời điểm hiện tại.** HIS tắt mặc định, giao diện ẩn thông tin HIS; ký số SmartCA (#1, #1a) tạm hoãn. Kiến trúc giữ sẵn interface để bật lại (ADR-017) |
| 10 | Hình thức ký khi chưa có SmartCA (11/11/2026) | **Người bệnh (hoặc người đại diện) ký tay trên máy tính bảng.** Ảnh chữ ký chèn vào PDF, lưu mã băm, khóa phiếu (mục 2.5a, ADR-018) |

**Giả định đang áp dụng**
- Giai đoạn 1 dùng **tài khoản nội bộ** của ứng dụng, chưa tích hợp AD/SSO.
- PDF đã ký lưu trên **thư mục ổ đĩa/thư mục mạng của máy chủ**; metadata và hash lưu trong SQL Server.

**Còn chờ bệnh viện cung cấp / trả lời**
1. **Phía bệnh viện khi ký tay** (cần trước Sprint 4): người ký theo cấu hình mẫu chỉ **xác nhận bằng tài khoản** (PDF ghi họ tên, chức danh, thời điểm xác nhận), hay **ký tay thêm** trên máy tính bảng. Đề xuất: xác nhận bằng tài khoản, vì nhanh hơn với 500–3.000 phiếu/ngày và gắn chặt với danh tính đăng nhập.
2. **Duyệt [mẫu phiếu đề xuất](mau-phieu-de-xuat.md)** (nội dung, ngưỡng điểm té ngã, khối chữ ký), hoặc gửi mẫu giấy đang dùng (đã xóa thông tin thật). Cách hiểu phiếu TT-01 đã được xác nhận.
3. Trên Windows Server chạy bằng **IIS + SQL Server có sẵn** hay **Docker**? SQL Server đã có giấy phép/phiên bản nào?
4. Số năm lưu trữ cụ thể theo quy định HSBA áp dụng cho các loại phiếu này (pháp chế xác nhận).

*Không còn chờ (tạm hoãn theo quyết định 9):* thông tin SmartCA UAT và code mẫu VNPT; tài liệu API FPT eHospital; danh sách lý do/quan hệ ký thay (dùng khi có SmartCA).

---

## 11. Bước tiếp theo

**Sprint 3** (mẫu phiếu, lập phiếu, sinh PDF) dựng trên [mẫu phiếu đề xuất](mau-phieu-de-xuat.md); góp ý của bệnh viện chỉ thay nội dung mẫu, không chặn tiến độ. Tiếp theo là **Sprint 4: ký tay trên máy tính bảng** (mục 2.5a).
