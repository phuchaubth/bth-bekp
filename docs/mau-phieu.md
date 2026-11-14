# Mẫu phiếu của bệnh viện (khởi tạo sẵn)

> Cập nhật 13/11/2026. Bốn mẫu dưới đây được chuyển từ bản Word bệnh viện gửi (song ngữ Việt – Anh) sang định nghĩa
> mẫu phiếu của ứng dụng (`backend/src/Consent.Application/ConsentTemplates/Defaults/*.json`). Bản Word gốc **không**
> được đưa vào mã nguồn.
>
> Nội dung các mẫu là văn bản công khai của bệnh viện (tên bệnh viện, số đường dây nóng, website, email chăm sóc
> khách hàng), không chứa dữ liệu người bệnh. Mọi dữ liệu trong kiểm thử là giả lập.

| Mã mẫu | Bản Word gốc | Tên phiếu | Phía bệnh viện xác nhận | Người bệnh chọn ý kiến? |
|---|---|---|---|---|
| `QT-KNGT-BM02` | 19. SONG NGỮ QT-KNGT-BM02 | Giấy đồng thuận về chế độ thanh toán và quyền lợi khám chữa bệnh BHYT | Tiếp đón ("Nhân viên hướng dẫn") | Không, chỉ ký xác nhận |
| `TTNB-NGOAITRU` | 27. BV Phiếu thông tin người bệnh ngoại trú | Phiếu thông tin người bệnh ngoại trú | Tiếp đón ("Nhân viên tiếp nhận") | Không, chỉ ký xác nhận |
| `QT-KNGT-BM10` | 30. JCI Phiếu nhận định ban đầu NB ngoại trú (02.08.2024) | Lần lượt: nhân viên hướng dẫn (Tiếp đón) → điều dưỡng → **bác sĩ xác nhận cuối** | Không, chỉ ký xác nhận |
| `TUVAN-CONGTT` | 24. JCI Phiếu tư vấn tham gia và sử dụng cổng thông tin | Lần lượt: người tư vấn (Tiếp đón) → **điều dưỡng khoa xác nhận cuối** | Có: "Đồng ý (Agree)" / "Không đồng ý (Disagree)" |

Mã `TTNB-NGOAITRU` và `TUVAN-CONGTT` do nhóm phát triển đặt vì bản Word không ghi mã biểu mẫu; bệnh viện đã đồng ý dùng
hai mã này (13/11/2026).

## 1. Cách chuyển từ bản giấy

- **Thông tin hành chính** (họ tên, ngày sinh, giới tính, mã người bệnh, CCCD, điện thoại, địa chỉ) in tự động ở mục
  "I. Thông tin người bệnh" từ hồ sơ, nên không lặp lại trong nội dung riêng của mẫu.
- **Ngày tháng và chữ ký** do hệ thống in: người bệnh ký tay trên điện thoại; nhân viên phía bệnh viện đăng nhập bằng tài khoản và cũng ký tay khi xác nhận (quyết định 17). PDF có ảnh chữ ký, họ tên, chức danh và thời điểm của từng người.
  Các dòng "Ngày … tháng … năm …" trên giấy được thay bằng ngày lập và thời điểm ký.
- **Ô đánh dấu** chuyển thành câu hỏi chọn một hoặc chọn nhiều. Dòng "Có, ghi rõ…" mở thêm ô nhập chỉ khi chọn "Có".
- **Song ngữ** giữ nguyên: nhãn tiếng Việt kèm tiếng Anh trong ngoặc, như bản giấy. Trên PDF và trên màn hình, **tiếng Việt
  màu đen, tiếng Anh màu xanh (`#0070C0`) và chữ nghiêng**. Hệ thống tự nhận phần tiếng Anh:
  - đoạn trong ngoặc gần như không có chữ có dấu (cho phép tên riêng tiếng Việt trong ngoặc kép “…”);
  - dòng riêng của tiêu đề/phụ đề không có chữ có dấu nào ("Consent Form").

  Đơn vị viết thường như "(kg)", điểm số, mã mẫu và dữ liệu người dùng nhập giữ màu thường. Khi soạn mẫu mới, cứ ghi tiếng
  Anh trong ngoặc như hiện nay, không cần đánh dấu gì thêm.
- **Tự điền từ hồ sơ khi lập phiếu:** số thẻ BHYT (BM02); dân tộc, nghề nghiệp, địa chỉ thường trú, chỗ ở hiện nay
  (phiếu thông tin ngoại trú). Người lập vẫn sửa được trước khi trình ký.

Để in đúng các phiếu này, định nghĩa mẫu được mở rộng (xem [API, mục 6b](api.md)):

- `signatures.decision = "none"`: người bệnh chỉ ký xác nhận, không có câu hỏi Đồng ý/Từ chối trên máy tính bảng và PDF.
- `signatures.decisionLabel`, `agreeLabel`, `refuseLabel`: câu hỏi và nhãn riêng, dùng cho phiếu tư vấn cổng thông tin.
- Nguồn tự điền mới: `patient.permanentAddress`, `patient.currentAddress`, `patient.ethnicity`, `patient.occupation`.
- `signatures.steps`: **nhiều bước xác nhận theo vai trò** (bệnh viện đồng ý 13/11/2026). Sau khi người bệnh ký, nhân viên
  đúng vai trò của từng bước lần lượt bấm "Xác nhận: …" trên trang chi tiết phiếu; người có vai trò xác nhận cuối hoàn tất
  phiếu khi đủ các bước. Mỗi bước một người khác nhau (một tài khoản không xác nhận hai phần trên cùng phiếu). PDF in một
  ô chữ ký cho mỗi người, kèm họ tên, chức danh và thời điểm xác nhận.

## 2. Từng mẫu

### 2.1. QT-KNGT-BM02 – Giấy đồng thuận (chế độ thanh toán, quyền lợi BHYT)

- Toàn văn nội dung tư vấn: trường hợp không BHYT, trường hợp có BHYT với ba ý con.
- Câu hỏi bắt buộc **chế độ khám chữa bệnh**: Bảo hiểm y tế / Không Bảo hiểm y tế.
- Chọn BHYT thì bắt buộc **số thẻ BHYT** (mã thẻ mới 17 ký tự; mã cũ 15 ký tự vẫn nhận, phiếu nháp hiện cảnh báo nhẹ), tự điền từ hồ sơ.
- Cam đoan "không khiếu kiện gì về sau" in ngay trước khối chữ ký.

### 2.2. TTNB-NGOAITRU – Phiếu thông tin người bệnh ngoại trú

- Dân tộc, tôn giáo (Phật giáo / Thiên chúa giáo / Khác, ghi rõ), trình độ văn hóa, nghề nghiệp, địa chỉ thường trú,
  chỗ ở hiện nay.
- Người nhà: họ tên, quan hệ, số điện thoại. Hỏi "Đây có phải người liên hệ khẩn cấp không?". Chọn "Không" thì bắt
  buộc họ tên, quan hệ, số điện thoại của người liên hệ khẩn cấp.
- Lời cam đoan của người khai. Nhãn ký bên bệnh viện kèm ghi chú "đã đối chiếu thông tin trên giấy tờ tùy thân".

### 2.3. QT-KNGT-BM10 – Phiếu nhận định ban đầu và nhu cầu GDSK

Một phiếu điện tử gồm đủ các phần của bản giấy, theo thứ tự:

1. **Người bệnh tự khai:** phòng khám; nguy cơ té ngã (2 câu); tầm soát bệnh lây nhiễm (câu 2, 3 chỉ hiện khi câu 1 là
   "Có").
2. **Nhân viên hướng dẫn:** 2 câu té ngã, hành động can thiệp; xử trí tầm soát lây nhiễm (chỉ hiện khi người bệnh có
   triệu chứng). Nhân viên hướng dẫn xác nhận phần này bằng tài khoản (bước 1).
3. **Điều dưỡng:** dấu hiệu sinh tồn và chỉ số đo (mạch, nhiệt độ, huyết áp dạng `120/80`, nhịp thở, chiều cao, cân
   nặng bắt buộc; SpO₂, vòng đầu, cân nặng 3 tháng trước, % sụt cân không bắt buộc), có kiểm tra định dạng số.
   **BMI tự tính** = cân nặng (kg) / [chiều cao (m)]², làm tròn 1 chữ số thập phân (ví dụ 58,5 kg, 165 cm → 21,5),
   hiện ngay khi nhập; không nhập tay được, máy chủ tính lại khi lưu (khối `computed`, công thức `bmi`).
   Dấu hiệu cần cấp cứu: chọn "Có" thì ghi rõ và **bỏ qua toàn bộ phần đánh giá** (đúng ghi chú "chỉ thực hiện nếu mục A
   không có dấu hiệu cần cấp cứu").
4. **Đánh giá:** lý do đến khám, dị ứng, trạng thái tinh thần, 6 trở ngại chăm sóc (mỗi mục "Có" thì giải thích), tình
   trạng sinh sống, đánh giá đau (thời gian, vị trí, điểm 0–10, thang NRS / Wong-Baker / FLACC / CRIES), nguy cơ té ngã
   cao.
5. **Sàng lọc dinh dưỡng**, chọn đối tượng:
   - Người lớn: điểm BMI (0/1/2), % sụt cân (0/1/2), bệnh lý cấp hoặc gần như không ăn > 5 ngày (0/2). Hệ thống tự cộng
     tổng 0–6 và xếp mức: 0 không nguy cơ, 1 trung bình, 2–4 cao, ≥ 5 rất cao.
   - Bệnh nhi < 16 tuổi (PNST): 4 câu Có/Không, tự đếm số yếu tố nguy cơ; 0–1 kết thúc đánh giá, ≥ 2 có nguy cơ.
6. **Nhu cầu GDSK do điều dưỡng thực hiện** (phòng ngừa té ngã, chăm sóc vết thương). Điều dưỡng xác nhận bằng tài
   khoản (bước 2).
7. **Phần bác sĩ:** mức nguy cơ suy dinh dưỡng và giải pháp (người lớn hoặc nhi), tư vấn chế độ ăn (6 lựa chọn, "Khác"
   thì ghi rõ), nhu cầu GDSK khác (té ngã, vết thương, đau, người bệnh mới, khác).

Bác sĩ chọn mức nguy cơ dinh dưỡng cuối cùng, vì bản giấy có điều kiện ngoài tổng điểm (BMI < 16,0 hoặc > 30 là nguy cơ
rất cao).

### 2.4. TUVAN-CONGTT – Phiếu tư vấn tham gia và sử dụng cổng thông tin

- Phần tư vấn: đoạn giới thiệu, 5 đường dây nóng, 6 kênh cung cấp thông tin và đăng ký khám.
- Phần ý kiến: khi ký, người bệnh/người đại diện chọn "Đồng ý (Agree)" hoặc "Không đồng ý (Disagree)" cho câu hỏi "Ông/bà
  đã xem và đồng ý tham gia sử dụng các Cổng thông tin chính thức tại bệnh viện?". Ý kiến được thống kê như các phiếu
  đồng ý/từ chối khác.
- Hai mã QR trên bản giấy không đưa vào, vì định nghĩa mẫu chưa hỗ trợ hình ảnh.

## 3. Giới hạn hiện tại

| Nội dung | Bản giấy | Ứng dụng hiện nay | Đề xuất |
|---|---|---|---|
| Nhập liệu theo từng phần (BM10) | Mỗi người tự ghi phần của mình vào phiếu giấy | Nội dung điền ở bản nháp (nhân viên cùng khoa đều sửa được), khóa khi trình ký; sau khi người bệnh ký, từng người xác nhận phần của mình bằng tài khoản | Khóa riêng từng phần theo vai trò nếu bệnh viện cần |
| Phạm vi khoa | | Người xác nhận phải thấy được phiếu: cùng khoa với phiếu, hoặc có quyền toàn viện. Nhân viên hướng dẫn, điều dưỡng, bác sĩ cần thuộc cùng khoa (ví dụ Khoa Khám bệnh) | |
| Mã QR, logo trên phiếu | Có | Không in. Đầu trang dùng tên đơn vị cấu hình ở *Quản trị → Cấu hình hệ thống* | Bổ sung khối hình ảnh nếu cần |
| Bố cục bảng, ô vuông | Bảng nhiều cột | Danh sách câu hỏi tuần tự; PDF dùng ô ■/□ | |
| Số trang PDF | BM02, phiếu tư vấn: 1 trang | 2 trang, vì khối chữ ký luôn nằm trọn một trang | Thu gọn bố cục nếu bệnh viện yêu cầu 1 trang |

## 4. Điểm khác so với bản Word

> Bệnh viện đã đồng ý các cách hiểu dưới đây (13/11/2026), gồm cả hai mã `TTNB-NGOAITRU`, `TUVAN-CONGTT`.

1. **BM10, bảng BMI người lớn:** ô điểm 0 ghi "≥ 20%". Ứng dụng hiểu là BMI ≥ 20 (không có dấu %).
2. **Phiếu tư vấn cổng thông tin:** email ghi "ctxh_bth@bthh.org.vn" ở một chỗ, "ctxh_bth@bth.org.vn" ở chỗ khác. Ứng
   dụng dùng **ctxh_bth@bth.org.vn** (trùng tên miền website).
3. **Phiếu tư vấn cổng thông tin:** dòng ứng dụng di động bị ghi nhầm nhãn "Đường dây nóng bệnh viện" ở cột bên. Ứng dụng
   ghi đúng nội dung "Đăng ký khám chữa bệnh qua ứng dụng di động".
4. **BM02:** sửa lỗi chính tả "chênh lệnh" thành "chênh lệch".
5. **BM10, trạng thái tinh thần:** bản giấy ghi "Đáp ứng phù hợp: ☐ Không ☐ Có, ghi rõ". Ứng dụng giữ nguyên (chọn "Có"
   thì ghi rõ). Nếu ý đúng là "Không thì ghi rõ", cần sửa mẫu.
6. Mã chính thức cho hai phiếu chưa có mã (`TTNB-NGOAITRU`, `TUVAN-CONGTT`), và lần ban hành, ngày hiệu lực in trên PDF.

## 5. Khi bệnh viện muốn sửa

- Sửa câu chữ, thêm/bớt câu hỏi, đổi ngưỡng điểm: người quản lý mẫu tạo **phiên bản mới** ở màn hình *Mẫu phiếu* (soạn
  JSON, xem trước), rồi phát hành. Phiếu đã lập giữ đúng phiên bản cũ.
- **Cấu hình ký** (thẻ *Cấu hình ký* của phiên bản nháp, không cần sửa JSON): thêm, xóa, đổi thứ tự các bước xác nhận
  (tối đa 5, mỗi bước chọn vai trò và tên ô ký), chọn hỏi Đồng ý/Không đồng ý hay chỉ ký xác nhận, câu hỏi và nhãn lựa
  chọn, tên các ô ký trên PDF. Ô để trống dùng nhãn mặc định. Lưu xong phải **phát hành** phiên bản thì phiếu mới mới áp
  dụng; phiếu đã lập ký theo phiên bản của nó.
- **Vai trò xác nhận cuối** (người bấm "Xác nhận và hoàn tất"), tên mẫu, đang sử dụng hay không: nút **Sửa thông tin
  mẫu**. Vai trò xác nhận cuối gắn với mẫu, không theo phiên bản, nên đổi là **áp dụng ngay** cho cả phiếu đang chờ xác
  nhận (màn hình có cảnh báo).
- Cơ sở dữ liệu đã chạy với ba mẫu đề xuất cũ (CK-01, TT-01, TN-01): khởi động lại sẽ **thêm** bốn mẫu mới, không xóa mẫu
  cũ. Quản trị viên đặt các mẫu cũ thành "ngừng dùng" để không còn được chọn khi lập phiếu; phiếu đã lập theo mẫu cũ vẫn
  xem và in được.
