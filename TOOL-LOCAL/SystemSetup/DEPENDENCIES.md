# Dependency Setup — 2026-09-14

- Qwen: dùng nguyên `VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km`, URI revision/size/SHA-256/license hiện có. Không có bản checksum thứ hai trong catalog Setup.
- Piper ONNX/config và uv: dùng các artifact đã pin trong `VietsubVoiceComponentStore`; provenance/license hiện hành ở `third_party/voice` được giữ nguyên.
- Python mới: CPython **3.11.15 x64**, tải bằng uv **0.12.3** đã kiểm SHA-256. Danh mục Python và checksum bản phân phối đến từ metadata tích hợp trong bản uv đã pin; không dùng Python trong PATH hoặc registry. Phiên bản runtime v2 cài `.venv` ngay trong thư mục cuối, không di chuyển venv sau cài.
- Piper và toàn bộ dependency: `piper-requirements.in` và `piper-requirements.lock`, resolve Windows/CPython 3.11.15 bằng uv đã pin, nguồn `https://pypi.org/simple`, chỉ wheel, `--require-hashes`. Worker kiểm Python và các phiên bản thực cài trước khi nạp model. Marker ràng buộc hash worker/requirements/Python/model/config và máy/tài khoản Windows.
- Không đọc cấu hình uv/pip của người dùng; loại biến môi trường UV/PIP/PYTHON kế thừa trước khi chạy uv, đặt cache/Python directory riêng và index cố định. uv xử lý outbound của Python/PyPI; đây không phải HTTP client download Qwen/Piper của C#. Không khẳng định exact host allowlist của C# tự bảo vệ mạng của uv.
- OCR/media: runtime/model và FFmpeg đi cùng bundle. Setup kiểm OCR bằng ảnh raster Anh/Trung, kiểm manifest FFmpeg rồi tạo/probe WAV mẫu.
- WebView2: kiểm trước giao diện; nếu thiếu, hộp thoại WinForms dẫn đến trang Microsoft và cho kiểm tra lại sau cài Evergreen x64. Không tải/chạy bootstrapper không có checksum/signature đã duyệt.

Tài liệu chính thức đã đối chiếu: [uv require-hashes](https://docs.astral.sh/uv/reference/cli/), [nguồn Python của uv](https://docs.astral.sh/uv/concepts/python-versions/), [Microsoft WebView2 distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution).

Đây là khóa kỹ thuật để kiểm thử, không thay thế phê duyệt phân phối thư viện. Hồ sơ FFmpeg vẫn có scope Development; cần nghiệm thu Windows sạch, license/provenance và phát hành theo quy trình trước khi gọi bộ cài là release-ready.
