# Áp dụng database Dịch Cloud Vietsub

- Thời điểm: 2026-09-10, Asia/Bangkok (UTC+07); migration đích hoàn tất khoảng 00:38:40.
- Đích xác minh từ cấu hình dự án và SQL runtime: `DUNGDEV / VideoFactory`, Windows authentication, SQL Server `15.0.2000.5`.
- Phạm vi: chỉ migration `database/VideoFactory.4.1.6.VietsubCloudTranslation.sql`. Database đã có các migration TikTok/lip-sync/local voice của nhánh khác; không chạy lại hoặc xóa lịch sử đó.
- Backup mới: `D:\SQL2019\Microsoft SQL Server\MSSQL15.MSSQLSERVER\MSSQL\Backup\VideoFactory_before_cloud_20260910_003325.bak` (4,59 MiB), `COPY_ONLY`, `CHECKSUM`, `COMPRESSION`; `RESTORE VERIFYONLY` Passed.
- Khôi phục thực sang `VideoFactory_CloudRehearsal_20260910_003325` Passed; `DBCC CHECKDB` Passed. Bản sao đã được dọn sau kiểm chứng; database đích và backup được giữ lại.
- Lần rehearsal đầu phát hiện thiếu `QUOTED_IDENTIFIER` khi tạo filtered index bằng sqlcmd. Transaction rollback trên clone; bổ sung bộ SET options bắt buộc vào migration mới trước khi áp đích.
- Chạy migration đã sửa hai lần trên clone Passed; chỉ đổi câu `USE` sang tên clone trong bản script tạm. Đối chiếu 81 bảng bằng số dòng và checksum trên các cột cũ: chỉ `ai.SchemaVersions` thêm bản ghi `4.1.6`.
- Áp migration một lần trên đích Passed; kiểm ba bảng Cloud, cột nullable, FK video giữ nguyên, FK Vietsub/CHECK trusted và filtered unique index Passed.
- Dữ liệu trước/sau trên đích: 81 bảng cũ không thay đổi ngoài version mới; 62 reservation và 178 ledger được giữ nguyên. Ba bảng Cloud đều 0 dòng. Database permissions và role membership không thay đổi.
- Test `VietsubCloudMigrationTests`: 1 Passed / 0 Failed / 0 Skipped. `git diff --check` Passed. Không thay đổi application source trong bước database nên không chạy lại toàn bộ build/test ứng dụng.
- `VietsubCloudTranslation.Enabled=false`, `ModelCode` vẫn trống. Chưa gọi OpenAI, cấu hình credential/rate/budget hoặc khởi động lại server/desktop.
- Còn mở: rehearsal cạnh tranh nhiều instance, toàn chuỗi migration trên baseline thấp nhất, smoke OpenAI và nghiệm thu ứng dụng thật. Kết quả bước này xác nhận migration trên database local hiện hành.

Manifest chứa hash migration/backup và các mốc xác minh. Log SQL và script dùng trong phiên nằm cùng thư mục; không có connection string, API key hoặc transcript. Backup nằm ngoài repository trong thư mục backup của SQL Server.
