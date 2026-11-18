# Mẫu phiếu của bệnh viện (khởi tạo sẵn)

> Cập nhật 18/11/2026. Bốn mẫu dưới đây được chuyển từ bản Word bệnh viện gửi (song ngữ Việt – Anh) sang định nghĩa
> mẫu phiếu của ứng dụng (`backend/src/Consent.Application/ConsentTemplates/Defaults/*.json`), rồi chỉnh theo bản giấy
> bệnh viện gửi sau (BM02 15/11, TTNB 16/11, BM10 và phiếu tư vấn 18/11). Bản Word, bản giấy gốc **không** được đưa vào
> mã nguồn.
>
> Nội dung các mẫu là văn bản công khai của bệnh viện (tên bệnh viện, số đường dây nóng, website, email chăm sóc
> khách hàng), không chứa dữ liệu người bệnh. Mọi dữ liệu trong kiểm thử là giả lập.

| Mã mẫu | Bản Word gốc | Tên phiếu | Phía bệnh viện xác nhận | Người bệnh chọn ý kiến? |
|---|---|---|---|---|
| `QT-KNGT-BM02` | 19. SONG NGỮ QT-KNGT-BM02 | Giấy đồng thuận về chế độ thanh toán và quyền lợi khám chữa bệnh BHYT | Tiếp đón ("Nhân viên hướng dẫn") | Không, chỉ ký xác nhận |
| `TTNB-NGOAITRU` | 27. BV Phiếu thông tin người bệnh ngoại trú; bản giấy PDF mã QT-KNGT/BM03 (16/11/2026) | Phiếu thông tin người bệnh ngoại trú | Tiếp đón ("Nhân viên tiếp nhận") | Không, chỉ ký xác nhận |
| `QT-KNGT-BM10` | 30. JCI Phiếu nhận định ban đầu NB ngoại trú (02.08.2024); bản giấy PDF mã QT-KNGT/BM10, lần ban hành 08 (18/11/2026) | Lần lượt: nhân viên hướng dẫn (Tiếp đón) → điều dưỡng → **bác sĩ xác nhận cuối** | Không, chỉ ký xác nhận |
| `TUVAN-CONGTT` | 24. JCI Phiếu tư vấn tham gia và sử dụng cổng thông tin; bản giấy PDF mã QĐ-VHTT-BM02, lần ban hành 01 (18/11/2026) | **Người tư vấn xác nhận cuối** (vai trò ở *Sửa thông tin mẫu*, mặc định Điều dưỡng) | Có: "Đồng ý (Agree)" / "Không đồng ý (Disagree)" |

Mã `TTNB-NGOAITRU` và `TUVAN-CONGTT` do nhóm phát triển đặt vì bản Word không ghi mã biểu mẫu; bệnh viện đã đồng ý dùng
hai mã này (13/11/2026). Bản giấy TTNB gửi ngày 16/11/2026 có mã tài liệu QT-KNGT/BM03, bản giấy phiếu tư vấn gửi ngày
18/11/2026 có mã QĐ-VHTT-BM02: các mã này in ở đầu PDF; mã mẫu trong phần mềm vẫn là `TTNB-NGOAITRU`, `TUVAN-CONGTT` để
giữ các phiếu đã lập và cấu hình kiosk.

## 1. Cách chuyển từ bản giấy

- **Thông tin hành chính** (họ tên, ngày sinh, giới tính, mã người bệnh, CCCD, điện thoại, địa chỉ) in tự động ở mục
  "I. Thông tin người bệnh" từ hồ sơ, nên không lặp lại trong nội dung riêng của mẫu.
- **Đầu phiếu và mục I** (bệnh viện yêu cầu 15/11/2026, quyết định 28): `TTNB-NGOAITRU` và `TUVAN-CONGTT` không in dòng
  "Khoa/phòng: …" dưới tên bệnh viện (`hideDepartment`), vì bản giấy không có dòng này. Phiếu tư vấn in mục
  "I. Thông tin người bệnh" song ngữ (`bilingual`): "Họ và tên (Full name)", "Mã người bệnh (Patient ID)"… Từ khi có bản
  giấy (BM02, TTNB, BM10, phiếu tư vấn), cả bốn mẫu in các dòng thông tin người bệnh như bản giấy (`patientLines`) thay cho
  mục I, không in dòng khoa.
- **Ngày tháng và chữ ký** do hệ thống in: người bệnh ký tay trên điện thoại; nhân viên phía bệnh viện đăng nhập bằng tài khoản và cũng ký tay khi xác nhận (quyết định 17). PDF có ảnh chữ ký, họ tên, chức danh và thời điểm của từng người.
  Các dòng "Ngày … tháng … năm …" trên giấy được thay bằng ngày lập và thời điểm ký.
- **Ô đánh dấu** chuyển thành câu hỏi chọn một hoặc chọn nhiều. Dòng "Có, ghi rõ…" mở thêm ô nhập chỉ khi chọn "Có".
- **Song ngữ** giữ nguyên: nhãn tiếng Việt kèm tiếng Anh trong ngoặc, hoặc sau dấu "/" như bản giấy BM02
  ("Ngày sinh/ Date of birth"). Trên PDF và trên màn hình, **tiếng Việt màu đen, tiếng Anh màu xanh (`#0070C0`) và chữ
  nghiêng**. Hệ thống tự nhận phần tiếng Anh:
  - đoạn trong ngoặc gần như không có chữ có dấu (cho phép tên riêng tiếng Việt trong ngoặc kép “…”);
  - phần sau dấu "/" đến hết dòng, khi sau dấu "/" là khoảng trắng hoặc chữ hoa không dấu và phần đó là tiếng Anh
    ("Nam/Male"); dấu "/" giữa hai chữ tiếng Việt ("người bệnh/người nhà") hay trong mã ("QT-KNGT/BM02") không tách;
  - dòng riêng của tiêu đề/phụ đề không có chữ có dấu nào ("Consent Form").

  Đơn vị viết thường như "(kg)", điểm số, mã mẫu và dữ liệu người dùng nhập giữ màu thường. Khi soạn mẫu mới, cứ ghi tiếng
  Anh trong ngoặc như hiện nay, không cần đánh dấu gì thêm.
- **Tự điền từ hồ sơ khi lập phiếu:** số thẻ BHYT (BM02); dân tộc, nghề nghiệp, địa chỉ thường trú, chỗ ở hiện nay
  (phiếu thông tin ngoại trú). Người lập vẫn sửa được trước khi trình ký.

Để in đúng các phiếu này, định nghĩa mẫu được mở rộng (xem [API, mục 6b](api.md)):

- `signatures.decision = "none"`: người bệnh chỉ ký xác nhận, không có câu hỏi Đồng ý/Từ chối trên máy tính bảng và PDF.
- `signatures.decisionLabel`, `agreeLabel`, `refuseLabel`: câu hỏi và nhãn riêng, dùng cho phiếu tư vấn cổng thông tin.
- Nguồn tự điền mới: `patient.permanentAddress`, `patient.currentAddress`, `patient.ethnicity`, `patient.occupation`.
- `signatures.statement`: **ô lời xác nhận của người ký** ở khung ký (bệnh viện yêu cầu 14/11/2026), ví dụ "Tôi đã nghe,
  hiểu và đồng ý": các câu gợi ý hiện thành nút bấm chọn nhanh, vẫn sửa hoặc tự gõ được; có thể bắt buộc. Lời xác nhận in
  trên PDF ngay trên chữ ký (chữ đỏ, đậm nghiêng) và hiện trong lịch sử phiếu. Cả 4 mẫu thật bật ô này (bắt buộc); sửa ở
  thẻ *Cấu hình ký*.
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
- **PDF in theo bản giấy bệnh viện gửi 15/11/2026** (quyết định 29):
  - đầu phiếu: logo bên trái, quốc hiệu ở giữa, ô "Mã số (Code No): QT-KNGT/BM02 / Lần ban hành (Issue No): 09 / Ngày hiệu
    lực (Effective Date):" bên phải (`documentControl`; ngày hiệu lực bản giấy để trống);
  - tiêu đề 4 dòng: "GIẤY ĐỒNG THUẬN", "(Về chế độ … Bảo hiểm y tế)", "CONSENT FORM", "(Regarding …)";
  - thông tin người bệnh như bản giấy (`patientLines`): họ tên; ngày sinh và giới tính có ô Nam/Male, Nữ/Female; mã BN;
    số thẻ BHYT (trường của phiếu, in ở đầu phiếu, không BHYT thì để chấm trống);
  - không còn tiêu đề phần; câu chọn chế độ chỉ in hai ô, chữ đậm (`printOptionsOnly`);
  - song ngữ kiểu "Tiếng Việt/ English"; dòng ngày tháng và ghi chú ô ký kèm tiếng Anh (`bilingual`);
  - ô ký: "Nhân viên hướng dẫn/ Counseling Staff" bên trái, "Người thỏa thuận/ Declarant" bên phải, tên ô ký giữ chữ thường
    (`hospitalFirst`, `plainTitles`).
  - Khác bản giấy: sửa "chênh lệnh" thành "chênh lệch" (điểm 4 mục 4), bỏ dấu ")" thừa ở cuối câu "… as follows):"; giữ
    "Độc lập - Tự do - Hạnh phúc" theo thể thức. Dòng ngày tháng in ngày lập phiếu; phiếu đã ký có ảnh chữ ký, họ tên,
    thời điểm ký, số phiếu ở chân trang.
  - Địa danh ở dòng ngày tháng lấy từ *Cấu hình hệ thống*; muốn in "TP. Hồ Chí Minh (Ho Chi Minh city)" như bản giấy thì
    ghi đúng như vậy ở ô địa danh.

### 2.2. TTNB-NGOAITRU – Phiếu thông tin người bệnh ngoại trú

- Nhập liệu: dân tộc (tự điền từ hồ sơ), tôn giáo (Phật giáo / Thiên chúa giáo / Khác, ghi rõ), trình độ văn hóa, nghề
  nghiệp, địa chỉ thường trú, chỗ ở hiện nay (tự điền từ hồ sơ). Người nhà: họ tên, quan hệ, số điện thoại. Hỏi "Đây có phải
  người liên hệ khẩn cấp không?"; chọn "Không" thì bắt buộc họ tên, quan hệ, số điện thoại của người liên hệ khẩn cấp. Lời
  cam đoan của người khai.
- **PDF in theo bản giấy bệnh viện gửi 16/11/2026** (mã QT-KNGT/BM03, lần ban hành 08; quyết định 33):
  - đầu phiếu một hàng: logo bên trái, tên phiếu ở giữa ("PHIẾU THÔNG TIN NGƯỜI BỆNH NGOẠI TRÚ", dòng tiếng Anh màu xanh), ô
    "Mã số (Code): QT-KNGT/BM03 / Lần ban hành (Issue number): 08 / Ngày hiệu lực (Effective date):" bên phải; không có quốc
    hiệu, tên đơn vị, dòng khoa (`documentControl` với `titleInHead`; ngày hiệu lực bản giấy để trống);
  - 15 mục đánh số như bản giấy (`patientLines`, `numberedPatientLines`): họ tên, giới tính (ô Nam/Nữ), ngày sinh, số căn
    cước và số điện thoại lấy từ hồ sơ; dân tộc in ô "Kinh" / "Khác, ghi rõ" theo giá trị đã nhập; tôn giáo in ba ô và phần
    ghi rõ sau ô "Khác"; mục 15 in hai ô Có/Không và ba dòng của người liên hệ khẩn cấp thụt vào, để chấm khi người nhà là
    người liên hệ khẩn cấp; không còn mục "I. Thông tin người bệnh" và tiêu đề phần;
  - dòng ngày tháng canh giữa, chữ đứng, kèm "(day)", "(month)", "(year)";
  - ô ký: "Nhân viên tiếp nhận" bên trái với ghi chú "(đã đối chiếu thông tin trên giấy tờ tùy thân)", "(Patient Admission
    Staff)", "(verified against identification documents)" (dòng đối chiếu màu đỏ như bản giấy, giữ nguyên sau khi xác nhận);
    "Người khai (ký tên, ghi rõ họ tên)" bên phải với "(Declarant (signature, full name))", "Nếu không phải người bệnh, ghi rõ
    quan hệ với người bệnh", "(If not the patient, state relationship to the patient)". Người đại diện ký thì họ tên kèm quan
    hệ in dưới chữ ký.
  - Phiếu đã ký (kể cả người đại diện ký, có khung chữ ký) vừa một trang A4 như bản giấy.
  - Khác bản giấy: lề trang của phiếu điện tử rộng hơn nên tên phiếu cỡ 12 (bản giấy cỡ 14), ô mã tài liệu cỡ 9 (bản giấy cỡ
    10), ghi chú ô ký cỡ 11; không in các dòng chấm để viết tay mà in giá trị đã nhập (ô trống thì in dòng chấm); thêm dấu
    ")" còn thiếu ở "(Date of Birth (as per ID Card / Citizen ID))"; dấu ngoặc của phần tiếng Anh cùng màu xanh với chữ (bản
    giấy để ngoặc màu đen); dòng "(đã đối chiếu…)" in nghiêng; người đại diện ký thì in thêm một dòng "Người ký thay mặt người
    bệnh" (họ tên, quan hệ, số CCCD) dưới mục 15, như các mẫu khác.

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
   - Người lớn: điểm BMI (0/1/2; **tự chọn theo BMI tự tính**: ≥ 20 → 0, 18,5 đến dưới 20 → 1, dưới 18,5 → 2, so với BMI đã
     làm tròn như in trên phiếu; chưa có BMI thì chọn tay), % sụt cân (0/1/2), bệnh lý cấp hoặc gần như không ăn > 5 ngày (0/2). Hệ thống tự cộng
     tổng 0–6 và xếp mức: 0 không nguy cơ, 1 trung bình, 2–4 cao, ≥ 5 rất cao.
   - Bệnh nhi < 16 tuổi (PNST): 4 câu Có/Không, tự đếm số yếu tố nguy cơ; 0–1 kết thúc đánh giá, ≥ 2 có nguy cơ.
6. **Nhu cầu GDSK do điều dưỡng thực hiện** (phòng ngừa té ngã, chăm sóc vết thương). Điều dưỡng xác nhận bằng tài
   khoản (bước 2).
7. **Phần bác sĩ:** mức nguy cơ suy dinh dưỡng và giải pháp (người lớn hoặc nhi), tư vấn chế độ ăn (6 lựa chọn, "Khác"
   thì ghi rõ), nhu cầu GDSK khác (té ngã, vết thương, đau, người bệnh mới, khác).

Bác sĩ chọn mức nguy cơ dinh dưỡng cuối cùng, vì bản giấy có điều kiện ngoài tổng điểm (BMI < 16,0 hoặc > 30 là nguy cơ
rất cao).

**PDF in theo bản giấy bệnh viện gửi 18/11/2026** (mã QT-KNGT/BM10, lần ban hành 08; quyết định 43):

- đầu phiếu: cột trái in cơ quan chủ quản và tên bệnh viện phía trên logo (`unitInHead`), tên phiếu 6 dòng ở giữa (dòng
  "(Outpatient – Department of Medical Examination)" chữ thường, nghiêng), ô "Mã số (Code): QT-KNGT/BM10 / Lần ban hành
  (Issue No): 08 / Ngày hiệu lực (Effective Date):" bên phải; không có dòng khoa;
- thông tin người bệnh như bản giấy: họ tên; ngày sinh và giới tính (ô Nam/Nữ); phòng khám (trường của phiếu); địa chỉ;
- **bốn phần như bản giấy**, mỗi phần một trang, tên phần trong dải nền màu (`banner`, `newPage`): người bệnh tự khai (té
  ngã, tầm soát lây nhiễm); nhân viên hướng dẫn (té ngã, hành động can thiệp, xử trí lây nhiễm); điều dưỡng (A. dấu hiệu
  sinh tồn, B. đánh giá 1–8 gồm cả sàng lọc dinh dưỡng, C. nhu cầu GDSK); bác sĩ (A. đánh giá dinh dưỡng, B. nhu cầu GDSK
  khác). Mục 4–7 ở trên nay nằm trong phần điều dưỡng, như bản giấy;
- tiêu đề mục ("A - NGUY CƠ TÉ NGÃ (FALL RISK)"…) in đậm, canh giữa (`heading`); câu hỏi Có/Không, PNST và nhu cầu GDSK
  in thành **bảng kẻ ô**, mỗi lựa chọn một ô (`layout: "row"`); phần "ghi rõ", "giải thích" in tiếp trên dòng câu hỏi;
- **mỗi phần kết thúc bằng ô ký của người thực hiện** (`signer`): người bệnh, nhân viên hướng dẫn, điều dưỡng, bác sĩ;
  dòng "Ngày (Day) … tháng (month) … năm (year) …" là ngày người đó ký hoặc xác nhận (chưa ký thì để chấm); tên ô ký đậm,
  giữ chữ thường (`boldTitles`). Không còn khối chữ ký cuối phiếu;
- 6 trang như bản giấy, kể cả khi in khung chữ ký. Người đại diện ký và khung chữ ký cao (ví dụ tỉ lệ 4:3) thì phần 1
  dài hơn một trang: ô ký phần 1 sang trang sau, ở cả bản xem trước và bản đã ký.
- Khác bản giấy: dấu hiệu sinh tồn mỗi chỉ số một dòng (bản giấy hai cột); phần "ghi rõ" in kèm nhãn của trường, ví dụ
  "☒ Có, ghi rõ (Yes, specify) Ghi rõ (Specify): Penicillin"; câu đang ẩn (ví dụ PNST khi đánh giá người lớn) không in;
  mã trường, điều kiện hiển thị và cách tính điểm giữ như trước, nên phiếu đã lập, kế thừa phiếu lần trước vẫn dùng được.

### 2.4. TUVAN-CONGTT – Phiếu tư vấn tham gia và sử dụng cổng thông tin

- Phần tư vấn: đoạn giới thiệu, 5 đường dây nóng, 6 kênh cung cấp thông tin và đăng ký khám, khung "Xin Quý người bệnh và
  thân nhân lưu ý" 3 điều.
- Phần ý kiến: khi ký, người bệnh/người đại diện chọn "Đồng ý (Agree)" hoặc "Không đồng ý (Disagree)" cho câu hỏi "Ông/bà
  đã xem và đồng ý tham gia sử dụng các Cổng thông tin chính thức tại bệnh viện?". Ý kiến được thống kê như các phiếu
  đồng ý/từ chối khác.
- **PDF in theo bản giấy bệnh viện gửi 18/11/2026** (mã QĐ-VHTT-BM02, lần ban hành 01; quyết định 43):
  - đầu phiếu một hàng như TTNB: logo, tên phiếu 4 dòng ở giữa, ô "Mã số (Code): QĐ-VHTT-BM02 / Lần ban hành (Issue
    number): 01 / Ngày hiệu lực (Effective date):" bên phải; không có dòng khoa;
  - họ tên, ngày sinh, giới tính (ô Nam/Nữ), địa chỉ mỗi mục một dòng (`patientLines`), không còn mục "I. Thông tin người
    bệnh" (nên không còn hai mục số I);
  - khung lưu ý nền xám viền xanh (`box`), các điều đánh số chữ nghiêng; câu "Xin vui lòng đánh dấu chéo "X" …";
  - **hai ô ký như bản giấy**: "Người bệnh/Người đại diện của người bệnh (Patient/Patient’s Representative)" với ghi chú
    "(Ký và ghi rõ họ tên)", "(Ghi rõ quan hệ với người bệnh)", và "Người tư vấn (*) (Consultant)"; tên ô ký đậm, giữ chữ
    thường. Người tư vấn là **người xác nhận cuối**: bỏ bước trung gian "Người tư vấn" (Tiếp đón) và ô "Điều dưỡng khoa" của
    bản trước. Bản giấy ghi người tư vấn là nhân viên chăm sóc khách hàng (người bệnh ngoại trú khám lần đầu) hoặc điều dưỡng
    nội trú (người bệnh nội trú nhập viện lần đầu): đặt vai trò xác nhận cuối ở *Sửa thông tin mẫu*, ví dụ một vai trò tự tạo
    "Người tư vấn" gán thêm cho các nhân viên này (quyết định 40);
  - "Hướng dẫn thực hiện phiếu (Instructions for completing the form)" in cuối phiếu, chữ nhỏ nghiêng (`footnote`);
  - 3 trang như bản giấy.
  - Khác bản giấy: mã QR (mã đáp ứng nhanh, tải ứng dụng) chưa in, vì định nghĩa mẫu chưa hỗ trợ hình ảnh; câu "Chân thành
    cám ơn sự hợp tác của ông (bà)" in trước câu hỏi ý kiến (ô Đồng ý/Không đồng ý do khối chữ ký in ngay sau); dòng ngày
    tháng in ngày lập phiếu, giờ ký in dưới từng chữ ký (bản giấy để "…giờ…phút" viết tay); dòng Facebook dùng email
    ctxh_bth@bth.org.vn (điểm khác 2 ở mục 4).

## 3. Giới hạn hiện tại

| Nội dung | Bản giấy | Ứng dụng hiện nay | Đề xuất |
|---|---|---|---|
| Nhập liệu theo từng phần (BM10) | Mỗi người tự ghi phần của mình vào phiếu giấy | Nội dung điền ở bản nháp (nhân viên cùng khoa đều sửa được), khóa khi trình ký; sau khi người bệnh ký, từng người xác nhận phần của mình bằng tài khoản | Khóa riêng từng phần theo vai trò nếu bệnh viện cần |
| Phạm vi khoa | | Người xác nhận phải thấy được phiếu: cùng khoa với phiếu, hoặc có quyền toàn viện. Nhân viên hướng dẫn, điều dưỡng, bác sĩ cần thuộc cùng khoa (ví dụ Khoa Khám bệnh) | |
| Mã QR, logo trên phiếu | Có | Logo tải ở *Quản trị → Cấu hình hệ thống* in ở đầu phiếu; mẫu có `documentControl` (cả bốn mẫu) in thêm ô mã tài liệu như bản giấy. Mã QR chưa in | Bổ sung khối hình ảnh nếu cần |
| Bố cục bảng, ô vuông | Bảng nhiều cột | Câu hỏi Có/Không in thành bảng kẻ ô (`layout: "row"`, BM10); câu hỏi khác tuần tự; PDF dùng ô ☐, ô đã chọn có dấu X ☒ như đánh dấu trên giấy (quyết định 30) | |
| Số trang PDF | BM02: 1 trang | BM02: 2 trang, vì khối chữ ký luôn nằm trọn một trang. Phiếu tư vấn 3 trang, BM10 6 trang như bản giấy (cả khi in khung chữ ký; người đại diện ký và khung cao thì 7 trang) | Thu gọn bố cục nếu bệnh viện yêu cầu |

## 4. Điểm khác so với bản Word

> Bệnh viện đã đồng ý các cách hiểu dưới đây (13/11/2026), gồm cả hai mã `TTNB-NGOAITRU`, `TUVAN-CONGTT`.

1. **BM10, bảng BMI người lớn:** ô điểm 0 ghi "≥ 20%". Ứng dụng hiểu là BMI ≥ 20 (không có dấu %).
2. **Phiếu tư vấn cổng thông tin:** email ghi "ctxh_bth@bthh.org.vn" ở một chỗ, "ctxh_bth@bth.org.vn" ở chỗ khác. Ứng
   dụng dùng **ctxh_bth@bth.org.vn** (trùng tên miền website). Bản giấy QĐ-VHTT-BM02 (18/11/2026) vẫn ghi
   "ctxh_bth@bthh.org.vn" ở dòng Facebook; ứng dụng vẫn dùng ctxh_bth@bth.org.vn.
3. **Phiếu tư vấn cổng thông tin:** dòng ứng dụng di động bị ghi nhầm nhãn "Đường dây nóng bệnh viện" ở cột bên (bản giấy
   QĐ-VHTT-BM02 cũng vậy). Ứng dụng ghi đúng nội dung "Đăng ký khám chữa bệnh qua ứng dụng di động".
4. **BM02:** sửa lỗi chính tả "chênh lệnh" thành "chênh lệch".
5. **BM10, trạng thái tinh thần:** bản giấy ghi "Đáp ứng phù hợp: ☐ Không ☐ Có, ghi rõ". Ứng dụng giữ nguyên (chọn "Có"
   thì ghi rõ). Nếu ý đúng là "Không thì ghi rõ", cần sửa mẫu.
6. Mã chính thức cho hai phiếu chưa có mã (`TTNB-NGOAITRU`, `TUVAN-CONGTT`), và lần ban hành, ngày hiệu lực in trên PDF.
   Cả bốn mẫu đã in mã số, lần ban hành theo bản giấy (`documentControl`): BM02 (lần 09), TTNB (QT-KNGT/BM03, lần 08), BM10
   (lần 08), phiếu tư vấn (QĐ-VHTT-BM02, lần 01); ngày hiệu lực để trống như bản giấy.

## 5. Khi bệnh viện muốn sửa

- Sửa câu chữ, thêm/bớt câu hỏi, đổi ngưỡng điểm: người quản lý mẫu tạo **phiên bản mới** ở màn hình *Mẫu phiếu* (soạn
  JSON, xem trước), rồi phát hành. Phiếu đã lập giữ đúng phiên bản cũ.
- Cách in trên PDF (bỏ dòng khoa ở đầu phiếu, mục I song ngữ, in hai trường cùng dòng, in theo bản giấy có mã tài liệu,
  mỗi phần một trang và người ký riêng, bảng Có/Không, khung lưu ý, ghi chú cuối phiếu): thêm `hideDepartment`, `bilingual`,
  `sameLine`, `documentControl`, `patientLines`, `banner`, `newPage`, `signer`, `layout: "row"`, `box`, `heading`,
  `footnote`… khi soạn JSON của phiên bản mới (xem [API, mục 6b](api.md)).
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
