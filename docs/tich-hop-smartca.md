# Thiết kế tích hợp VNPT SmartCA

> Trạng thái: **BẢN THIẾT KẾ – CHỜ PHÊ DUYỆT**. Chưa có mã nguồn.
> Nguồn: hai tài liệu VNPT do bệnh viện cung cấp:
> - *Tài liệu tích hợp VNPT SmartCA* v4.0 (03/05/2024), API cho bên thứ ba theo QĐ 769/QĐ-BTTTT, gọi tắt là **API SP769**.
> - *Dịch vụ VNPT-SmartCA – Tài liệu đặc tả và hướng dẫn tích hợp* v1.1 (15/05/2024), API OAuth2/CSC, gọi tắt là **API OAuth2**.
>
> Hai file PDF gốc **không được đưa vào repo**: đây là tài liệu của VNPT, và các ví dụ trong đó có thông tin cá nhân cùng thông tin xác thực mẫu. Tài liệu này cũng không chép lại bất kỳ giá trị mẫu nào trong đó.

---

## 1. Chọn API

| Tiêu chí | API SP769 (v4.0) | API OAuth2 (v1.1) | SmartCA TH (SP769 v2: ký không cần app) |
|---|---|---|---|
| Xác thực | `sp_id` + `sp_password` của bệnh viện, gửi trong body | Mỗi người ký phải đăng nhập SmartCA (Authorization code) hoặc đưa mật khẩu cho ứng dụng (ROPC) | Như SP769, cộng thêm **mật khẩu + OTP của người ký** |
| Định danh người ký | `user_id` = **số CCCD** | `credentialId` lấy sau khi người dùng đăng nhập | `user_id` = số CCCD |
| Người ký xác nhận | Trên app VNPT SmartCA | Trên app VNPT SmartCA | Không cần app (ký tự động bằng OTP) |
| Phù hợp người bệnh vãng lai | **Rất phù hợp**: chỉ cần CCCD vừa quét QR | Kém: phải đăng nhập web SmartCA, refresh token lưu 3 tháng | Không phù hợp: phải thu mật khẩu/OTP của người bệnh |
| Phù hợp bác sĩ | Phù hợp | Có thể dùng | Có thể dùng sau này nếu pháp chế chấp thuận |

**Quyết định đề xuất:** dùng **API SP769 (v4.0), luồng ký có xác nhận trên app** cho **tất cả người ký** (người bệnh, người ký thay, nhân viên chứng kiến, bác sĩ).

- Không dùng API OAuth2 trong giai đoạn 1.
- Không dùng SmartCA TH, vì ứng dụng sẽ phải giữ mật khẩu và khóa OTP của người ký, làm tăng rủi ro bảo mật và yếu hơn về chống chối bỏ.

---

## 2. Thông tin kết nối (cấu hình qua biến môi trường)

| Biến | Giá trị / ghi chú |
|---|---|
| `SmartCa__Provider` | `Fake` (mặc định dev/test/CI) hoặc `Vnpt` |
| `SmartCa__BaseUrl` | UAT: `https://rmgateway.vnptit.vn/sca/sp769`; Production: `https://gwsca.vnpt.vn/sca/sp769` |
| `SmartCa__SpId` | Do VNPT cấp cho bệnh viện |
| `SmartCa__SpPassword` | **Bí mật**: chỉ lưu trong biến môi trường/secret store, không commit, không ghi log |
| `SmartCa__TransactionTimeoutSeconds` | Mặc định 300 (tài liệu cho thấy giao dịch hết hạn sau khoảng 5 phút) |
| `SmartCa__PollIntervalSeconds` | Mặc định 3 |
| `SmartCa__WebhookEnabled` | `false` mặc định (xem mục 5) |

Bệnh viện cần đăng ký với VNPT để nhận **cặp thông tin kết nối riêng cho môi trường UAT và Production**. Tài khoản người dùng thử nghiệm (UAT) tạo bằng thông tin của **nhân viên tự nguyện thử nghiệm**, không dùng người bệnh thật và không lưu những thông tin này trong repo.

---

## 3. Các API SP769 sử dụng

Tất cả đều là `POST`, `Content-Type: application/json`, phản hồi có dạng `{ status_code, message, data }`.

| Bước | Endpoint | Mục đích | Tham số chính |
|---|---|---|---|
| 1 | `/v1/credentials/get_certificate` | Lấy danh sách chứng thư của người ký | `sp_id`, `sp_password`, `user_id` (CCCD), `transaction_id` (duy nhất), `serial_number` (tùy chọn) |
| 2 | `/v1/signatures/sign` | Gửi yêu cầu ký hash; người ký nhận thông báo trên app | `user_id`, `transaction_id` (duy nhất), `transaction_desc`, `serial_number`, `time_stamp` (`YYYYMMddHHmmSS`), `sign_files[] { file_type: "pdf", sign_type: "hash", data_to_be_signed (hex), doc_id }` |
| 3 | `/v1/signatures/sign/{tranId}/status` | Kiểm tra trạng thái và lấy chữ ký | `tranId` trả về từ bước 2 |
| (4) | Webhook do bệnh viện cung cấp | VNPT báo kết quả sau khi người ký xác nhận | `sp_id`, `status_code`, `message`, `transaction_id`, `signed_files[] { doc_id, signature_value, timestamp_signature }` |

Mã lỗi cần xử lý và thông báo tiếng Việt tương ứng:

| Mã | Tên | Thông báo cho người dùng |
|---|---|---|
| 200 | `SUCCESS` / `sig_wait_for_user_confirm` | "Vui lòng mở ứng dụng VNPT SmartCA và xác nhận giao dịch mã **{doc_id}**" |
| 400 | `BAD_REQUEST` | "Yêu cầu ký không hợp lệ. Vui lòng thử lại hoặc liên hệ CNTT." (ghi log chi tiết) |
| 401 | `SP_CREDENTIAL_INVALID` | Tài khoản người ký không hợp lệ → "Người ký chưa có tài khoản SmartCA hợp lệ", gợi ý phương án 2/3. Nếu lỗi do `sp_id`/`sp_password` → báo CNTT, không hiển thị chi tiết |
| 403 | `CREDENTIAL_STATUS_INVALID` | "Chứng thư số không hợp lệ hoặc đã hết hạn", gợi ý phương án 2/3 |
| 500 | `SERVAR_INTERNAL_ERROR` | "Hệ thống SmartCA đang gặp sự cố, vui lòng thử lại sau" (thử lại có giới hạn) |

---

## 4. Luồng ký một lượt (áp dụng cho mọi người ký)

```
Ứng dụng (API)                                   VNPT SmartCA (SP769)          App SmartCA người ký
     │ 1. get_certificate(user_id = CCCD)               │                              │
     │ ───────────────────────────────────────────────► │                              │
     │ ◄─── user_certificates[]                         │                              │
     │ 2. Chọn chứng thư: service_type = SMARTCA,       │                              │
     │    đang hoạt động, còn hạn, CCCD trong subject   │                              │
     │    khớp người ký                                 │                              │
     │ 3. Chuẩn bị PDF: thêm ô chữ ký + placeholder,    │                              │
     │    tính hash của dữ liệu cần ký (SHA-256)        │                              │
     │ 4. sign(data_to_be_signed = hash hex,            │                              │
     │         doc_id = mã hiển thị, transaction_id)    │                              │
     │ ───────────────────────────────────────────────► │ ── thông báo ──────────────► │
     │ ◄─── sig_wait_for_user_confirm, tranId           │                              │
     │ 5. Màn hình hiển thị doc_id + đếm ngược          │ ◄── người ký xác nhận ────── │
     │ 6. Poll status/{tranId} (và/hoặc nhận webhook)   │                              │
     │ ───────────────────────────────────────────────► │                              │
     │ ◄─── signatures[] { doc_id, signature_value }    │                              │
     │ 7. Ghép chữ ký + chứng thư + chuỗi CA thành      │                              │
     │    CMS (PAdES), chèn vào placeholder             │                              │
     │ 8. Kiểm tra lại chữ ký trong PDF, lưu file mới,  │                              │
     │    SHA-256, thông tin chứng thư, audit           │                              │
```

Chi tiết các bước:

1. **Chọn chứng thư:**
   - Bỏ qua chứng thư `ESEAL` (con dấu tổ chức) và chứng thư không còn hiệu lực.
   - Kiểm tra `cert_valid_to` còn hạn.
   - Kiểm tra `cert_subject` có `UID=CCCD:<số CCCD người ký>` hoặc `CMND:` tương ứng. Không khớp thì chặn ký. Với phương án 3, đây là bước bảo đảm người ký thay đúng là người đã khai báo.
   - Nếu có nhiều chứng thư hợp lệ, chọn chứng thư hết hạn muộn nhất và truyền `serial_number` của nó.
2. **`doc_id`:** VNPT hiển thị `doc_id` trên app SmartCA, và màn hình của bệnh viện hiển thị cùng mã đó. Dùng mã ngắn, dễ đọc, không chứa thông tin cá nhân, ví dụ `PDT-2026-000123-1` (số phiếu + lượt ký). Người ký phải so khớp mã trước khi xác nhận.
3. **`transaction_id`:** dùng Id (UUID) của bản ghi `SignatureTransactions`, bảo đảm duy nhất. Mỗi lần thử lại tạo giao dịch mới.
4. **`transaction_desc`:** ví dụ "Phiếu đồng thuận PDT-2026-000123 – Bệnh viện …". Không đưa chẩn đoán hay thông tin bệnh vào đây.
5. **Thời gian chờ:** đếm ngược 300 giây. Hết giờ thì đánh dấu giao dịch `Expired` và cho phép gửi lại.

---

## 5. Webhook và polling

- **Mặc định dùng polling** (`status/{tranId}` mỗi 3 giây, trong tối đa 300 giây). Cách này chạy được cả khi máy chủ bệnh viện không mở Internet chiều vào.
- **Webhook là tùy chọn.** Tài liệu chưa mô tả cơ chế ký hay xác thực webhook (chỉ có `sp_id`), vì vậy:
  - Webhook chỉ được coi là **tín hiệu kích hoạt**. Kết quả chính thức luôn lấy lại bằng `status/{tranId}`. Không tin `signature_value` trong webhook nếu chưa đối chiếu.
  - Endpoint webhook có đường dẫn khó đoán, kiểm tra `sp_id` và `transaction_id` có tồn tại và đang chờ ký, giới hạn IP nếu VNPT cung cấp dải IP, xử lý idempotent, ghi audit.
- Việc polling chạy trong một background service ở backend (`IHostedService`). Frontend chỉ hỏi trạng thái của **ứng dụng mình**, không gọi trực tiếp VNPT.

---

## 6. Ký PDF theo 2 pha (deferred signing) – điểm rủi ro kỹ thuật chính

SmartCA **chỉ hỗ trợ ký hash**, và người ký có thể mất tới 5 phút để xác nhận. Vì vậy PDF phải được ký theo 2 pha, qua nhiều request:

1. **Chuẩn bị:** thêm ô chữ ký (hình thức hiển thị: họ tên, vai trò, thời điểm, "Ký bởi VNPT SmartCA"), dành chỗ trống cho `/Contents`, tính `/ByteRange`. Lưu file tạm và trạng thái giao dịch.
2. **Hoàn tất:** khi nhận `signature_value`, dựng cấu trúc CMS/PKCS#7 (chứng thư người ký + chuỗi CA từ `get_certificate`), chèn vào chỗ trống, rồi kiểm tra lại.

Với nhiều lượt ký (người bệnh/người chứng kiến/người ký thay rồi bác sĩ), mỗi lượt là một **incremental update** trên file kết quả của lượt trước.

**Những điểm cần xác minh bằng spike kỹ thuật trên môi trường UAT** (dùng code mẫu của VNPT để đối chiếu):

- `data_to_be_signed` là hash của *signed attributes* (theo chuẩn CMS) hay hash của byte range? Tài liệu chỉ ghi "hex với hash", ví dụ là 64 ký tự hex (SHA-256).
- `signature_value` là chữ ký RSA thô hay CMS hoàn chỉnh?
- `{tranId}` ở API status là `transaction_id` do ứng dụng tạo hay `tran_code` do VNPT trả về?
- Mã `status_code`/`message` cụ thể khi người ký từ chối hoặc giao dịch hết hạn (tài liệu SP769 chưa liệt kê; API OAuth2 có `EXPIRED = 4001`, `SIGNER_REJECTED = 4002`… để tham khảo).
- `timestamp_signature` có được trả về không? Nếu có, nhúng dấu thời gian để đạt PAdES-B-T. Nếu không, cân nhắc dịch vụ TSA riêng.

**Thư viện:** PDFsharp (MIT) để tạo PDF, BouncyCastle (MIT) để dựng CMS. Nếu code mẫu .NET của VNPT có thư viện ký hash dùng được, cần **kiểm tra giấy phép** trước khi dùng. Không dùng iText (AGPL/thương mại).

> Đề xuất: làm **spike ký 2 pha ngay khi có tài khoản UAT** (song song Sprint 1–2), không đợi tới Sprint 4, vì đây là rủi ro kỹ thuật lớn nhất của dự án.

---

## 7. Thiết kế trong Clean Architecture

```
Application
  └─ Abstractions/Signing
       IDigitalSignatureService
         GetCertificatesAsync(signerCccd, ct)               → IReadOnlyList<SignerCertificate>
         RequestSignatureAsync(SignHashRequest, ct)         → SignRequestResult (ProviderTranId, ExpiresAt)
         GetSignatureStatusAsync(providerTranId, ct)        → SignStatus (Pending/Signed/Rejected/Expired/Failed, SignatureValue?)
       IPdfSigningPreparer
         PrepareAsync(pdf, signatureFieldInfo, cert, ct)    → PreparedPdf (HashToSign, PlaceholderInfo)
         CompleteAsync(preparedPdf, signatureValue, chain, ct) → SignedPdf
       IPdfSignatureValidator

Infrastructure
  └─ Signing
       VnptSmartCa/
         VnptSmartCaOptions          (BaseUrl, SpId, SpPassword…; validate khi khởi động)
         VnptSmartCaClient           (HttpClient typed, timeout, retry có giới hạn cho lỗi 5xx/mạng)
         VnptSmartCaSignatureService : IDigitalSignatureService
         Dtos (snake_case JSON)
       Fake/
         FakeSignatureService        (tự sinh chứng thư test, mô phỏng: thành công / chờ / từ chối / hết hạn / không có tài khoản)
       Pdf/
         PdfSigningPreparer, PdfSignatureValidator (PDFsharp + BouncyCastle)

Api
  └─ Webhooks/SmartCaWebhookController (chỉ khi WebhookEnabled)
  └─ HostedServices/SignaturePollingService
```

Quy tắc an toàn trong code:

- Request/response tới VNPT **không được ghi nguyên văn vào log**. Che `sp_password`, `user_id` (CCCD) và `data_to_be_signed` trước khi log.
- `HttpClient` dùng TLS mặc định của hệ điều hành, không tắt kiểm tra chứng chỉ.
- Mọi lần gọi VNPT đều lưu `SignatureTransactions` (trạng thái, mã lỗi, thời gian) và audit log.
- Gửi số CCCD của người ký sang VNPT là cần thiết để ký. Nội dung này cần được nêu trong thông báo xử lý dữ liệu cá nhân của bệnh viện (pháp chế xác nhận).

---

## 8. Kiểm thử

| Loại | Nội dung |
|---|---|
| Unit | Chọn chứng thư (bỏ ESEAL, hết hạn, sai CCCD); ánh xạ mã lỗi sang thông báo tiếng Việt; dựng request đúng định dạng (snake_case, `time_stamp`, `sign_type = hash`); che dữ liệu khi log |
| Unit (HTTP giả lập) | `VnptSmartCaClient` với `HttpMessageHandler` giả: 200/400/401/403/500, timeout, JSON thiếu trường |
| Integration | Luồng ký đầy đủ với `FakeSignatureService` + PDF thật: 2 chữ ký số hợp lệ, incremental update không làm hỏng chữ ký trước |
| Thủ công trên UAT | Ký bằng tài khoản thử nghiệm của nhân viên; mở PDF bằng Adobe Reader/Foxit để xác nhận chữ ký hợp lệ |

CI **không bao giờ** gọi tới hệ thống VNPT thật.

---

## 9. Câu hỏi cho bệnh viện / VNPT

1. Bệnh viện đã được VNPT cấp **`sp_id`/`sp_password` cho UAT** chưa? (Gửi qua kênh bảo mật cho người vận hành, **không gửi qua chat hay đưa vào repo**.)
2. Có thể lấy **code mẫu** từ link Google Drive trong tài liệu không? Code mẫu viết bằng ngôn ngữ nào, giấy phép thế nào? Code mẫu sẽ giúp trả lời các câu hỏi ở mục 6.
3. **Đã xác nhận:** hợp đồng **cho phép người bệnh dùng SmartCA cá nhân của họ** ký trên hệ thống bệnh viện. Còn cần làm rõ: **lượt ký tính vào gói của ai** (bệnh viện hay người bệnh)? Nếu tính vào gói của người bệnh, cần xử lý lỗi hết lượt ký (mã `65000 SERVICE_PACK_EXEED_OR_INVALID` trong tài liệu OAuth2) bằng cách gợi ý phương án 3/2.
4. VNPT có cung cấp **dải IP gọi webhook** hoặc cơ chế xác thực webhook không?
5. Có dịch vụ **dấu thời gian (TSA)** đi kèm không?
