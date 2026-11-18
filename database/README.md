# Database

SQL Server, database riêng `BEKP`, schema `bekp`.

- `migrations/001_initial_inpatient_operations.sql` – script **idempotent** sinh từ EF Core migration `InitialInpatientOperations`. DBA có thể chạy trực tiếp bằng `sqlcmd`/SSMS.
- Sinh lại script sau khi thêm migration:

```bash
cd backend
dotnet tool restore
dotnet ef migrations script --project BEKP.Persistence --startup-project BEKP.Persistence --idempotent -o ../database/migrations/<số>_<tên>.sql
```

Khuyến nghị: login riêng cho BEKP với quyền `db_datareader`, `db_datawriter` trên database `BEKP`; login migration tách riêng.
