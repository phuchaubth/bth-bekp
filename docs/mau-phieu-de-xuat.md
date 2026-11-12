# Mẫu phiếu đề xuất — giai đoạn 1

> Trạng thái: **ĐỀ XUẤT, chờ bệnh viện duyệt** (11/11/2026).
> Bệnh viện chưa gửi mẫu giấy đang dùng, nên ba mẫu dưới đây được soạn để Sprint 3 không phải chờ. Câu chữ cam kết, căn cứ pháp lý và nội dung chuyên môn (thang điểm té ngã, biện pháp phòng ngừa) **cần Phòng Kế hoạch tổng hợp, Phòng Điều dưỡng và bộ phận pháp chế rà soát**. Mục 6 liệt kê các điểm cần bệnh viện xác nhận.
> Trong ứng dụng, mẫu phiếu là **dữ liệu cấu hình có phiên bản**: sửa câu chữ, thêm/bớt mục hay đổi ngưỡng điểm không cần sửa code. Phiếu đã lập luôn giữ đúng phiên bản mẫu tại thời điểm lập.
> Mọi tên, số giấy tờ trong ví dụ là dữ liệu giả lập.

| Mã mẫu | Tên phiếu | Người ký phía bệnh viện (đề xuất) |
|---|---|---|
| CK-01 | Giấy cam kết sử dụng dịch vụ khám bệnh, chữa bệnh | Nhân viên tiếp đón |
| TT-01 | Phiếu cung cấp thông tin và cam kết chung | Điều dưỡng hoặc bác sĩ cung cấp thông tin |
| TN-01 | Phiếu đánh giá nguy cơ té ngã và cam kết phối hợp phòng ngừa | Điều dưỡng đánh giá |

Bản xem trước khổ A4 (dữ liệu giả lập) được gửi kèm khi trình duyệt.

---

## 1. Quy ước chung cho cả 3 phiếu

### 1.1. Bố cục trang (A4 dọc)

1. **Đầu phiếu**:
   - Bên trái: cơ quan chủ quản, tên bệnh viện, khoa/phòng.
   - Bên phải: Quốc hiệu, tiêu ngữ.
   - Góc phải: mã mẫu (ví dụ `MS: CK-01`), số phiếu và mã QR của số phiếu, để tra cứu bản in.
2. **Tên phiếu** in hoa, căn giữa.
3. **Thông tin người bệnh** (khối dùng chung, mục 1.2).
4. **Nội dung riêng** của từng phiếu (mục 2, 3, 4).
5. **Địa danh, ngày tháng năm** và **khối chữ ký**: bên trái là người bệnh hoặc người đại diện; bên phải là người ký phía bệnh viện.
6. **Chân trang**: mã mẫu và phiên bản, số phiếu, số trang (`Trang 1/1`).

Chữ in dùng phông có đủ dấu tiếng Việt, cỡ 12–13. Mỗi phiếu nên vừa 1 trang A4. Phiếu đánh giá té ngã có thể sang trang 2 khi có nhiều biện pháp.

### 1.2. Khối thông tin người bệnh (tự điền)

| Trường | Nguồn | Ghi chú |
|---|---|---|
| Họ và tên | Hồ sơ người bệnh (quét QR CCCD) | In hoa trên phiếu |
| Ngày sinh, tuổi | Hồ sơ | Tuổi tự tính tại ngày lập phiếu |
| Giới tính | Hồ sơ | |
| Số CCCD/định danh cá nhân | Hồ sơ | In đầy đủ trên phiếu, vì phiếu là giấy tờ trong hồ sơ bệnh án. Trên màn hình danh sách vẫn che |
| Địa chỉ | Hồ sơ | Địa chỉ hiện tại; để trống thì lấy nơi thường trú |
| Số điện thoại | Hồ sơ | |
| Mã người bệnh | Hồ sơ | Hiện nhập tay vì chưa kết nối HIS |
| Khoa/phòng | Người lập phiếu chọn | Mặc định khoa của người lập |

Nhân viên **sửa được** các trường tự điền trước khi trình ký. Sau khi phiếu đã ký thì nội dung bị khóa.

### 1.3. Người ký phía người bệnh

- **Người bệnh tự ký** khi đã thành niên và đủ năng lực hành vi dân sự.
- **Người đại diện hợp pháp ký** khi người bệnh chưa thành niên, mất hoặc hạn chế năng lực hành vi dân sự, hoặc tình trạng bệnh không cho phép ký.
  - Phiếu ghi rõ họ tên, quan hệ với người bệnh và số CCCD của người đại diện.
  - Thông tin này chọn từ danh sách người đại diện trong hồ sơ, đã quét CCCD ở màn hình 10.
- **Ngưỡng tuổi tự ký** và cách xử lý khi người bệnh không ký được (không biết chữ, khuyết tật…) **chờ pháp chế xác nhận** (mục 6).
- **Hình thức ký (đã chốt 11/11/2026): ký tay trên máy tính bảng.**
  - Ảnh chữ ký được chèn vào ô ký trên phiếu PDF, kèm họ tên và thời điểm ký.
  - Ô ký phía người bệnh vẫn in "(Ký, ghi rõ họ tên)" như mẫu giấy. Phía trên khối chữ ký là dòng "Ý kiến của người bệnh: □ Đồng ý □ Từ chối" (đánh dấu theo lựa chọn khi ký).
  - Ô phía bệnh viện in "(Đã xác nhận bằng tài khoản)" kèm họ tên, chức danh và thời điểm xác nhận (quyết định 11).
  - Quy trình chi tiết ở [kế hoạch, mục 2.5a](KE_HOACH_TRIEN_KHAI.md#25a-ký-tay-trên-máy-tính-bảng-đang-áp-dụng-quyết-định-10).

### 1.4. Kiểu trường dùng trong mẫu

| Kiểu | Ví dụ | Quy tắc |
|---|---|---|
| Văn bản / số / ngày | Số thẻ BHYT, giá trị thẻ | Có định dạng và độ dài kiểm tra như màn hình người bệnh |
| Chọn một | Hình thức khám; Đồng ý / Không đồng ý | Bắt buộc chọn rõ ràng, **không chọn sẵn** các mục đồng ý |
| Chọn nhiều / danh sách đánh dấu | Dịch vụ theo yêu cầu; nội dung đã cung cấp; biện pháp phòng ngừa | Có thể quy định "phải đánh dấu đủ" |
| Câu hỏi tính điểm | 6 câu thang Morse | Mỗi lựa chọn có điểm |
| Trường tự tính | Tuổi, tổng điểm, mức nguy cơ | Không sửa tay |
| Đoạn văn cố định | Nội dung cam kết | Sửa trong cấu hình mẫu, tạo phiên bản mới |
| Trường có điều kiện | Thông tin thẻ BHYT chỉ hiện khi chọn hình thức có BHYT | Ẩn thì không bắt buộc |

---

## 2. CK-01 — Giấy cam kết sử dụng dịch vụ khám bệnh, chữa bệnh

**Mục đích:** người bệnh chọn hình thức khám (BHYT, dịch vụ hoặc BHYT kết hợp dịch vụ theo yêu cầu) và cam kết về chi phí ngoài phạm vi quỹ BHYT chi trả.
**Thời điểm lập:** khi tiếp đón, trước khi khám. Nếu người bệnh đổi hình thức khám thì lập phiếu mới; phiếu cũ được giữ lại trong lịch sử.

### 2.1. Trường dữ liệu

| Mã | Nội dung | Kiểu | Bắt buộc |
|---|---|---|---|
| `examType` | Hình thức khám: ☐ Khám, chữa bệnh BHYT · ☐ Khám, chữa bệnh dịch vụ (tự chi trả toàn bộ) · ☐ BHYT kết hợp sử dụng dịch vụ theo yêu cầu (tự chi trả phần chênh lệch) | Chọn một | Có |
| `insuranceNumber` | Số thẻ BHYT (lấy từ hồ sơ nếu đã có) | Văn bản, 15 ký tự hoặc mã BHXH 10 số | Khi có BHYT |
| `insuranceValidFrom`, `insuranceValidTo` | Giá trị sử dụng của thẻ | Ngày | Khi có BHYT |
| `primaryCareFacility` | Nơi đăng ký khám, chữa bệnh ban đầu | Văn bản | Khi có BHYT |
| `referralPaper` | Giấy chuyển tuyến / giấy chuyển cơ sở khám, chữa bệnh: ☐ Có · ☐ Không · ☐ Bổ sung sau | Chọn một | Khi có BHYT |
| `requestedServices` | Dịch vụ theo yêu cầu: ☐ Chọn bác sĩ/chuyên gia · ☐ Chọn thời gian khám · ☐ Buồng bệnh theo yêu cầu · ☐ Khác: … | Chọn nhiều (danh mục cấu hình) | Khi chọn "BHYT kết hợp dịch vụ" |
| `acknowledged` | Tôi đã được giải thích, đọc và hiểu nội dung cam kết | Ô xác nhận | Có |

### 2.2. Nội dung cam kết (đoạn văn cố định)

> Tôi đã được nhân viên bệnh viện giải thích và hiểu rõ, và cam kết:
>
> 1. Giá dịch vụ khám bệnh, chữa bệnh được bệnh viện niêm yết công khai. Tôi được hỏi và được trả lời về chi phí trước khi sử dụng dịch vụ.
> 2. Khi khám, chữa bệnh BHYT: quỹ BHYT chi trả theo phạm vi và mức hưởng của thẻ. Tôi tự chi trả phần cùng chi trả, phần chênh lệch giá, và chi phí thuốc, vật tư, dịch vụ ngoài phạm vi BHYT.
> 3. Khi sử dụng dịch vụ theo yêu cầu: tôi tự nguyện lựa chọn và tự chi trả chi phí dịch vụ theo yêu cầu, hoặc phần chênh lệch so với mức BHYT chi trả.
> 4. Tôi xuất trình thẻ BHYT, giấy tờ tùy thân và giấy chuyển tuyến (nếu có) theo quy định. Nếu không xuất trình đủ trong thời hạn bệnh viện thông báo, tôi chi trả chi phí theo giá dịch vụ.
> 5. Tôi cung cấp thông tin trung thực, chấp hành nội quy bệnh viện và thanh toán đầy đủ chi phí theo quy định.
> 6. Khi muốn thay đổi hình thức khám, tôi báo nhân viên tiếp đón để lập cam kết mới.

**Ký:** Người bệnh/người đại diện · Nhân viên tiếp đón.

---

## 3. TT-01 — Phiếu cung cấp thông tin và cam kết chung

**Mục đích:** bệnh viện cung cấp các thông tin cần thiết cho người bệnh và ghi nhận các lựa chọn chung. Người bệnh hoặc người đại diện xác nhận đã được cung cấp, đã hiểu, và ghi nhận các lựa chọn đó.
**Đã xác nhận (11/11/2026):** đây là phiếu bệnh viện cung cấp thông tin, người bệnh xác nhận đã được cung cấp. Không phải phiếu người bệnh khai thông tin.
**Thời điểm lập:** lần đầu đến khám hoặc khi nhập viện. Có hiệu lực cho đến khi người bệnh thay đổi lựa chọn.

### 3.1. Phần A — Nội dung đã được cung cấp

Danh sách đánh dấu, mặc định **phải đánh dấu đủ** mới trình ký được:

1. Quyền và nghĩa vụ của người bệnh theo quy định của pháp luật về khám bệnh, chữa bệnh.
2. Nội quy bệnh viện, quy định về người nhà chăm sóc và giờ thăm (khi nằm viện).
3. Quy trình khám, chữa bệnh tại khoa/phòng và thời gian chờ dự kiến.
4. Giá dịch vụ, phạm vi quỹ BHYT chi trả, cách thanh toán; quyền nhận hóa đơn và bảng kê chi phí.
5. Thông tin của người bệnh được giữ bí mật; người bệnh có quyền chỉ định người được nhận thông tin về tình trạng bệnh.
6. Quyền được giải thích về tình trạng bệnh, phương pháp chẩn đoán và điều trị. Quyền từ chối điều trị hoặc xin ra viện (lập cam kết riêng).
7. An toàn người bệnh: nhận dạng người bệnh (vòng đeo tay), phòng ngừa té ngã, phòng cháy chữa cháy.
8. Kênh góp ý, phản ánh: đường dây nóng `[số đường dây nóng]`, hòm thư góp ý, khảo sát hài lòng.

### 3.2. Phần B — Người được nhận thông tin về tình trạng bệnh

- ☐ Tôi đồng ý bệnh viện cung cấp thông tin về tình trạng bệnh của tôi cho những người sau. Tối đa 2 người, mỗi người ghi họ tên, quan hệ, số điện thoại.
- ☐ Tôi không đồng ý cung cấp cho người khác, trừ trường hợp pháp luật quy định.

### 3.3. Phần C — Các lựa chọn đồng ý

Mỗi mục chọn **Đồng ý / Không đồng ý**, không chọn sẵn:

| Mã | Nội dung |
|---|---|
| `consentRoutineCare` | Đồng ý để bệnh viện thực hiện các xét nghiệm, chẩn đoán hình ảnh, thăm dò chức năng và chăm sóc thông thường cần cho việc khám, chữa bệnh. Thủ thuật, phẫu thuật, gây mê, truyền máu… **có phiếu đồng ý riêng**. |
| `consentContact` | Đồng ý để bệnh viện liên hệ qua số điện thoại đã cung cấp, để báo kết quả, nhắc lịch tái khám và khảo sát mức độ hài lòng. |
| `consentResearch` | Đồng ý để bệnh viện dùng thông tin của tôi, **đã được ẩn danh**, cho đào tạo, nghiên cứu khoa học và nâng cao chất lượng. Không đồng ý **không ảnh hưởng** quyền lợi khám, chữa bệnh. |

### 3.4. Phần D — Xác nhận

> Tôi đã được nhân viên y tế cung cấp đầy đủ các thông tin trên. Tôi đã đọc, hoặc được đọc cho nghe, đã hiểu, và đã được trả lời các câu hỏi. Tôi đồng ý với các lựa chọn đã ghi trong phiếu này. Tôi có thể thay đổi lựa chọn bất cứ lúc nào bằng cách báo nhân viên y tế.

**Ký:** Người bệnh/người đại diện · Nhân viên y tế cung cấp thông tin.

---

## 4. TN-01 — Phiếu đánh giá nguy cơ té ngã và cam kết phối hợp phòng ngừa

**Mục đích:**
- Đánh giá nguy cơ té ngã bằng **thang điểm Morse** (Morse Fall Scale; thang công khai, dùng rộng rãi).
- Ghi các biện pháp phòng ngừa đã áp dụng.
- Lấy cam kết phối hợp của người bệnh và người nhà.

**Đối tượng:** người bệnh người lớn. Trẻ em nên dùng thang dành riêng cho trẻ em (ví dụ Humpty Dumpty), lập thành mẫu khác nếu bệnh viện cần (mục 6).
**Thời điểm đánh giá:** lần đầu (tiếp nhận/nhập viện); khi tình trạng thay đổi; sau khi té ngã; đánh giá lại định kỳ theo quy định của khoa.

### 4.1. Thang điểm Morse

| # | Tiêu chí | Lựa chọn và điểm |
|---|---|---|
| 1 | Tiền sử té ngã (trong 3 tháng gần đây hoặc trong lần điều trị này) | Không: **0** · Có: **25** |
| 2 | Có từ 2 chẩn đoán bệnh trở lên | Không: **0** · Có: **15** |
| 3 | Hỗ trợ khi di chuyển | Không cần / nằm tại giường / có nhân viên y tế hỗ trợ: **0** · Nạng, gậy, khung tập đi: **15** · Phải vịn vào đồ vật xung quanh (giường, bàn, tường): **30** |
| 4 | Đang truyền dịch hoặc có đường truyền tĩnh mạch (kim luồn) | Không: **0** · Có: **20** |
| 5 | Dáng đi | Bình thường / nằm tại giường / ngồi xe lăn: **0** · Yếu: **10** · Suy giảm, không vững: **20** |
| 6 | Nhận thức | Biết đúng khả năng của bản thân: **0** · Đánh giá quá khả năng hoặc quên giới hạn của bản thân: **15** |
| | **Tổng điểm** (tự tính, 0–125) | |

**Mức nguy cơ** (ngưỡng đề xuất, cấu hình được, chờ Phòng Điều dưỡng xác nhận):

| Tổng điểm | Mức nguy cơ |
|---|---|
| 0–24 | Nguy cơ thấp |
| 25–44 | Nguy cơ trung bình |
| ≥ 45 | Nguy cơ cao |

Ngưỡng 0–24 / 25–44 / ≥ 45 được nhiều bệnh viện áp dụng. Bản gốc của thang Morse dùng 0–24 / 25–50 / ≥ 51. Bệnh viện chọn một trong hai.

### 4.2. Biện pháp phòng ngừa

Danh sách đánh dấu. Ứng dụng gợi ý sẵn theo mức nguy cơ, điều dưỡng xác nhận các mục đã thực hiện:

**Mọi người bệnh (biện pháp chuẩn)**
- Hướng dẫn cách gọi nhân viên y tế (chuông gọi).
- Giường ở vị trí thấp, khóa bánh xe; vật dụng cá nhân trong tầm với.
- Sàn khô, đủ ánh sáng; giày dép chống trơn trượt.
- Hướng dẫn người bệnh và người nhà về phòng ngừa té ngã.

**Nguy cơ trung bình (thêm)**
- Đánh dấu người bệnh có nguy cơ té ngã (biển báo, vòng tay màu theo quy định).
- Hỗ trợ khi di chuyển, khi đi vệ sinh; dựng thanh chắn giường khi cần.
- Đánh giá lại khi tình trạng thay đổi.

**Nguy cơ cao (thêm)**
- Người nhà ở cùng người bệnh 24/24 giờ, hoặc bố trí giường gần buồng trực.
- Kiểm tra người bệnh thường xuyên theo quy định của khoa.
- Rà soát thuốc làm tăng nguy cơ ngã (an thần, hạ huyết áp, lợi tiểu…) và báo bác sĩ điều trị.

Kèm theo: **thời điểm hẹn đánh giá lại** (bắt buộc khi nguy cơ trung bình hoặc cao).

### 4.3. Cam kết phối hợp (đoạn văn cố định)

> Tôi/gia đình đã được thông báo mức nguy cơ té ngã và được hướng dẫn các biện pháp phòng ngừa. Chúng tôi cam kết phối hợp:
> - Gọi nhân viên y tế khi cần hỗ trợ đi lại, đi vệ sinh.
> - Không tự ý hạ thanh chắn giường.
> - Luôn có người nhà ở cùng nếu được yêu cầu.
> - Báo ngay khi người bệnh chóng mặt, mệt, yếu hơn hoặc đã bị ngã.

**Ký:** Người bệnh/người nhà · Điều dưỡng đánh giá.

---

## 5. Quy tắc tự tính và kiểm tra (để làm Sprint 3)

| Quy tắc | Mô tả |
|---|---|
| Tuổi | Tính từ ngày sinh tới ngày lập phiếu |
| Người ký phía người bệnh | Mặc định "Người bệnh". Chọn "Người đại diện" thì bắt buộc chọn một người trong danh sách người đại diện của hồ sơ (họ tên, quan hệ, CCCD) |
| CK-01 | Chọn hình thức có BHYT thì bắt buộc số thẻ và giá trị thẻ. Hạn thẻ đã qua ngày lập phiếu thì cảnh báo, không chặn |
| TT-01 | Phần A phải đánh dấu đủ; phần B chọn một trong hai; phần C mỗi mục phải chọn rõ Đồng ý hoặc Không đồng ý |
| TN-01 | Đủ 6 câu mới tính tổng điểm; mức nguy cơ theo ngưỡng cấu hình. Nguy cơ trung bình/cao thì bắt buộc hẹn đánh giá lại và ít nhất một biện pháp ngoài biện pháp chuẩn |
| Khóa nội dung | Sau khi phiếu được ký, nội dung không sửa được. Muốn thay đổi thì lập phiếu mới (phiếu cũ thu hồi, có lý do, có nhật ký) |

---

## 6. Điểm cần bệnh viện xác nhận

1. **Thông tin chung:** tên bệnh viện, cơ quan chủ quản, địa danh ghi trên phiếu, logo (nếu có); quy ước đánh mã mẫu (`MS: …`).
2. **CK-01:**
   - Các hình thức khám và danh mục dịch vụ theo yêu cầu đang áp dụng.
   - Thời hạn xuất trình thẻ BHYT và giấy chuyển tuyến.
   - Có cần ghi số tiền tạm ứng hay không.
3. **TT-01** (cách hiểu đã được xác nhận):
   - Danh sách nội dung ở phần A và số đường dây nóng.
   - Có lấy đồng ý dùng dữ liệu cho đào tạo, nghiên cứu hay không. Mục này cần pháp chế rà soát theo quy định về bảo vệ dữ liệu cá nhân.
4. **TN-01:**
   - Dùng thang Morse cho người lớn; ngưỡng phân mức (mục 4.1).
   - Có cần mẫu riêng cho trẻ em không.
   - Danh sách biện pháp và tần suất đánh giá lại của từng khoa.
5. **Người ký:**
   - Ngưỡng tuổi người bệnh tự ký.
   - Cách xử lý khi người bệnh không ký được.
   - Vai trò ký phía bệnh viện của từng mẫu (đề xuất ở bảng đầu trang).
6. ~~Phía bệnh viện khi ký tay~~ — **đã chốt** (quyết định 11): chỉ xác nhận bằng tài khoản. Phía người bệnh ký tay trên máy tính bảng.
7. **Pháp chế rà soát** toàn bộ câu chữ cam kết và căn cứ pháp lý: Luật Khám bệnh, chữa bệnh số 15/2023/QH15; pháp luật về bảo hiểm y tế và bảo vệ dữ liệu cá nhân hiện hành.
