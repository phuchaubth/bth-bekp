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
| `QT-KNGT-BM10` | 30. JCI Phiếu nhận định ban đầu NB ngoại trú (02.08.2024) | Phiếu nhận định ban đầu và nhu cầu giáo dục sức khỏe người bệnh ngoại trú | Bác sĩ ("Bác sĩ thực hiện") | Không, chỉ ký xác nhận |
| `TUVAN-CONGTT` | 24. JCI Phiếu tư vấn tham gia và sử dụng cổng thông tin | Phiếu tư vấn tham gia và sử dụng cổng thông tin chính thức của bệnh viện | Tiếp đón ("Người tư vấn") | Có: "Đồng ý (Agree)" / "Không đồng ý (Disagree)" |

Mã `TTNB-NGOAITRU` và `TUVAN-CONGTT` do nhóm phát triển đặt vì bản Word không ghi mã biểu mẫu. Đổi mã là thay đổi dữ liệu
(tạo mẫu mới), nên cần bệnh viện cho mã chính thức trước khi phát hành ở môi trường thật.

## 1. Cách chuyển từ bản giấy

- **Thông tin hành chính** (họ tên, ngày sinh, giới tính, mã người bệnh, CCCD, điện thoại, địa chỉ) in tự động ở mục
  "I. Thông tin người bệnh" từ hồ sơ, nên không lặp lại trong nội dung riêng của mẫu.
- **Ngày tháng và chữ ký** do hệ thống in: người bệnh ký tay trên máy tính bảng, phía bệnh viện xác nhận bằng tài khoản.
  Các dòng "Ngày … tháng … năm …" trên giấy được thay bằng ngày lập và thời điểm ký.
- **Ô đánh dấu** chuyển thành câu hỏi chọn một hoặc chọn nhiều. Dòng "Có, ghi rõ…" mở thêm ô nhập chỉ khi chọn "Có".
- **Song ngữ** giữ nguyên: nhãn tiếng Việt kèm tiếng Anh trong ngoặc, như bản giấy.
- **Tự điền từ hồ sơ khi lập phiếu:** số thẻ BHYT (BM02); dân tộc, nghề nghiệp, địa chỉ thường trú, chỗ ở hiện nay
  (phiếu thông tin ngoại trú). Người lập vẫn sửa được trước khi trình ký.

Để in đúng các phiếu này, định nghĩa mẫu được mở rộng (xem [API, mục 6b](api.md)):

- `signatures.decision = "none"`: người bệnh chỉ ký xác nhận, không có câu hỏi Đồng ý/Từ chối trên máy tính bảng và PDF.
- `signatures.decisionLabel`, `agreeLabel`, `refuseLabel`: câu hỏi và nhãn riêng, dùng cho phiếu tư vấn cổng thông tin.
- Nguồn tự điền mới: `patient.permanentAddress`, `patient.currentAddress`, `patient.ethnicity`, `patient.occupation`.

## 2. Từng mẫu

### 2.1. QT-KNGT-BM02 – Giấy đồng thuận (chế độ thanh toán, quyền lợi BHYT)

- Toàn văn nội dung tư vấn: trường hợp không BHYT, trường hợp có BHYT với ba ý con.
- Câu hỏi bắt buộc **chế độ khám chữa bệnh**: Bảo hiểm y tế / Không Bảo hiểm y tế.
- Chọn BHYT thì bắt buộc **số thẻ BHYT** (15 ký tự hoặc mã BHXH 10 số), tự điền từ hồ sơ.
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
   triệu chứng); họ tên nhân viên.
3. **Điều dưỡng:** dấu hiệu sinh tồn và chỉ số đo (mạch, nhiệt độ, huyết áp dạng `120/80`, nhịp thở, chiều cao, cân
   nặng, BMI bắt buộc; SpO₂, vòng đầu, cân nặng 3 tháng trước, % sụt cân không bắt buộc), có kiểm tra định dạng số.
   Dấu hiệu cần cấp cứu: chọn "Có" thì ghi rõ và **bỏ qua toàn bộ phần đánh giá** (đúng ghi chú "chỉ thực hiện nếu mục A
   không có dấu hiệu cần cấp cứu").
4. **Đánh giá:** lý do đến khám, dị ứng, trạng thái tinh thần, 6 trở ngại chăm sóc (mỗi mục "Có" thì giải thích), tình
   trạng sinh sống, đánh giá đau (thời gian, vị trí, điểm 0–10, thang NRS / Wong-Baker / FLACC / CRIES), nguy cơ té ngã
   cao.
5. **Sàng lọc dinh dưỡng**, chọn đối tượng:
   - Người lớn: điểm BMI (0/1/2), % sụt cân (0/1/2), bệnh lý cấp hoặc gần như không ăn > 5 ngày (0/2). Hệ thống tự cộng
     tổng 0–6 và xếp mức: 0 không nguy cơ, 1 trung bình, 2–4 cao, ≥ 5 rất cao.
   - Bệnh nhi < 16 tuổi (PNST): 4 câu Có/Không, tự đếm số yếu tố nguy cơ; 0–1 kết thúc đánh giá, ≥ 2 có nguy cơ.
6. **Nhu cầu GDSK do điều dưỡng thực hiện** (phòng ngừa té ngã, chăm sóc vết thương) và họ tên điều dưỡng.
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
| Nhiều người ký trên một phiếu (BM10) | Người bệnh, nhân viên hướng dẫn, điều dưỡng, bác sĩ ký riêng từng phần | Người bệnh ký tay; **một** vai trò bệnh viện (bác sĩ) xác nhận bằng tài khoản. Nhân viên hướng dẫn và điều dưỡng ghi họ tên trong phiếu | Bổ sung "nhiều bước xác nhận" theo vai trò nếu bệnh viện cần chữ ký riêng từng phần |
| Phiếu tư vấn cổng thông tin | Ký: người bệnh, người tư vấn, điều dưỡng khoa | Người bệnh và người tư vấn (tiếp đón) | Như trên |
| Mã QR, logo trên phiếu | Có | Không in. Đầu trang dùng tên đơn vị cấu hình ở *Quản trị → Cấu hình hệ thống* | Bổ sung khối hình ảnh nếu cần |
| Bố cục bảng, ô vuông | Bảng nhiều cột | Danh sách câu hỏi tuần tự; PDF dùng ô ■/□ | |
| Số trang PDF | BM02, phiếu tư vấn: 1 trang | 2 trang, vì khối chữ ký luôn nằm trọn một trang | Thu gọn bố cục nếu bệnh viện yêu cầu 1 trang |

## 4. Điểm khác so với bản Word (cần bệnh viện xác nhận)

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
- Cơ sở dữ liệu đã chạy với ba mẫu đề xuất cũ (CK-01, TT-01, TN-01): khởi động lại sẽ **thêm** bốn mẫu mới, không xóa mẫu
  cũ. Quản trị viên đặt các mẫu cũ thành "ngừng dùng" để không còn được chọn khi lập phiếu; phiếu đã lập theo mẫu cũ vẫn
  xem và in được.
