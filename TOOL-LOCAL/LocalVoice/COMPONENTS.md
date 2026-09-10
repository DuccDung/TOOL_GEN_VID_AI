# Component profile: Veo local voice v1 (experimental)

Runtime CPU, Python 3.11.11, PyTorch/torchaudio 2.8.0+cpu, Silero VAD 6.2.1, Demucs 4.0.1 và OpenVoice V2 tone-color converter. Không gọi OpenVoice TTS API/MeloTTS, không gọi dịch vụ Cloud hoặc upload media.

Profile Windows tắt oneDNN/MKLDNN theo kết quả thử primitive của reference encoder trên i7-11800H; Demucs xử lý segment 4 giây để giảm peak RAM. Đây là cấu hình CPU cố định, không phải fallback provider.

## Provenance và checksum

| Thành phần | Phiên bản / nguồn | SHA-256 |
|---|---|---|
| OpenVoice source archive | [commit 74a1d147](https://github.com/myshell-ai/OpenVoice/tree/74a1d147b17a8c3092dd5430504bd83ef6c7eb23) | d08cbc84f4ec7abc76f9dddb5bbb221e906e49cfe5febd37133152bdeacb8be4 |
| OpenVoice converter checkpoint | [revision f36e7edf](https://huggingface.co/myshell-ai/OpenVoiceV2/tree/f36e7edfe1684461a8343844af60babc2efbb727/converter) | 9652c27e92b6b2a91632590ac9962ef7ae2b712e5c5b7f4c34ec55ee2b37ab9e |
| OpenVoice converter config | Cùng revision, converter/config.json | 9dfff60350b8c63f2c664efd92a61b2516efb22671466960f0e5dfebd881fa47 |
| Demucs htdemucs checkpoint | [official model](https://dl.fbaipublicfiles.com/demucs/hybrid_transformer/955717e8-8726e21a.th) | 8726e21a993978c7ba086d3872e7608d7d5bfca646ca4aca459ffda844faa8b4 |
| uv installer archive | [0.12.3 Windows x64](https://github.com/astral-sh/uv/releases/tag/0.12.3) | b23350c79e8ad0192b8124af13a0f17e8d4e4549524785e1aef389ae5a06990e |

Dependency archives được khóa toàn bộ version/hash trong Workers/requirements.lock; không dùng latest hoặc dependency tự dò khi inference. Managed Python do uv ghim 3.11.11 cài và được đưa vào installed-byte manifest. Installer giữ nguyên license trong OpenVoice source và metadata/license của wheel, không xóa attribution.

## License và giới hạn phát hành

- [OpenVoice source](https://github.com/myshell-ai/OpenVoice/blob/74a1d147b17a8c3092dd5430504bd83ef6c7eb23/LICENSE) dùng MIT; [model card OpenVoiceV2](https://huggingface.co/myshell-ai/OpenVoiceV2) khai báo MIT. Cần giữ các notice khi đóng gói.
- [Demucs upstream](https://github.com/facebookresearch/demucs/blob/e976d93ecc3865e5757426930257e200846a520a/LICENSE) khai báo MIT. Đây là mô hình tách nguồn nhạc/vocals, không phải bảo đảm separation hội thoại tiếng Việt. Release review phải ghi nhận riêng quyền phân phối checkpoint, không chỉ suy từ license code.
- [Silero VAD](https://github.com/snakers4/silero-vad) khai báo MIT; model được lấy từ wheel đã khóa. Các dependency khác giữ license đi kèm, đặc biệt Python/PyTorch/FFmpeg và thư viện âm thanh.
- Không dùng tài liệu này làm phê duyệt pháp lý/phát hành. Chưa có production package hoặc GPU profile được nghiệm thu. Không đóng gói audio demo hoặc media của người dùng vào bản phân phối.

## Integrity và môi trường

Installer xác minh size/hash của archive/model và hash dependency archive, rồi lập manifest byte của component thực dùng. Junction alias của managed Python không được traverse; venv dùng exact-version home. C# kiểm đường dẫn/reparse và mọi file trong manifest trước khi chạy Python. Worker nhận request riêng do host tạo, xác minh lại các model ghim; standalone worker không có xác nhận host thì kiểm toàn bộ manifest.

Đường cài có mạng chỉ để tải component. Runtime nhận môi trường tối thiểu, không nhận provider secret; chặn socket connect/connect_ex/sendto/getaddrinfo trước import model. Đây là guard trong tiến trình, không phải AppContainer hoặc firewall sandbox. Không nhận đường dẫn/model tùy ý từ WebView.

## Nghiệm thu còn yêu cầu

Technical probe/smoke không chứng minh người nói giống nhau, không sai từ tiếng Việt hoặc khẩu hình khớp. Phải nghe duyệt trên clip Veo có quyền sử dụng, đo thời gian/RAM và thử cảnh có ambient/SFX trước bật rộng.
