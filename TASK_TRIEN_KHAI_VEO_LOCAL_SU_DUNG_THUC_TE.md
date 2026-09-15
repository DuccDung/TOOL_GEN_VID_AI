Kế hoạch task để chạy đồng nhất giọng Veo local trên máy người dùng

Ngày lập: 2026-09-10. Dự án đích: `D:\laptrinhweb\code_outsrc\TOOL_AUTO_GEN_POST_VIDEO\TOOL_GEN_POST_VIDEO`, nhánh `local-3`.

Mục tiêu: người dùng mở đúng bản desktop của dự án này, chọn giọng từ một clip Veo đã duyệt, chuyển giọng các cảnh khác của cùng nhân vật, nghe duyệt và xuất được MP4. Bật đầy đủ điều kiện của luồng local theo yêu cầu người dùng. Đã triển khai cấu hình và runtime; trạng thái từng task được ghi bên dưới. Phần điểm xuất phát giữ lại bằng chứng trước thay đổi, không phải cấu hình hiện hành.

**Tiến độ triển khai ngày 2026-09-10**

| Task | Trạng thái hiện tại | Bằng chứng / phần còn lại |
|---|---|---|
| T01 | Đã kiểm tra database và chạy server; chờ chọn project nghiệm thu | SELECT chỉ đọc xác minh DUNGDEV/VideoFactory, version 4.1.8-local-voice-consistency, cột và constraint enabled/trusted. Có 5 project Fal Native Audio, chưa project nào bật local policy tại thời điểm kiểm tra. Server Release đúng repository đã chạy HTTPS 7242; đăng nhập trả 400 khi gửi JSON không hợp lệ, endpoint local-voice/access trả 401 khi chưa xác thực. Chưa đăng nhập bằng tài khoản thật. |
| T02 | Hoàn tất | Flag desktop đã bật; cấu hình native component/temp riêng trên D, đường dẫn đầy đủ và kiểm reparse. Môi trường installer/worker được lọc và dùng temp riêng; media workspace giữ nguyên. |
| T03 | Hoàn tất | Cài bằng installer ghim: Python, PyTorch CPU, Silero, Demucs, OpenVoice V2. Manifest 23.998 file, tổng byte được kiểm 3.970.101.008; runtime probe trả READY. Cài và probe lần đầu 309,8 giây. Có lệnh Prepare/Verify/Status qua chính host desktop. |
| T04 | Hoàn tất kiểm thử kỹ thuật | Python helper 7 Passed/0 Failed/0 Skipped. Cùng model test chạy 2 lượt liên tiếp trên cấu hình cuối, mỗi lượt 1 Passed/0 Failed/0 Skipped; anchor/conversion/remux/cache retry trong 228,4 và 254,1 giây. Mẫu là demo_speaker0/1 trong archive upstream đã ghim, không phải clip Veo tiếng Việt của người dùng. |
| T05 | Đã mở desktop; chờ thao tác project sau đăng nhập | Mo-VideoMaker.cmd tự mở server nền, kiểm HTTPS và runtime READY rồi mở desktop Release đúng repository. Đã xác minh cửa sổ VideoMaker phản hồi. Chưa xác minh panel WebView2 trong phiên đăng nhập thật. |
| T06 | Chờ chọn clip và nghe duyệt | Chưa tự chọn giọng, bật policy project, xác nhận quyền dùng giọng hoặc duyệt thay người dùng. Chưa export MP4 của project thật. |
| T07 | Regression kỹ thuật đạt; nghiệm thu ứng dụng còn lại | Unit/integration kiểm cancel/retry/stale/hash/render guard; model test kiểm retry cache. Còn đóng/mở ứng dụng, thao tác thật và nghe A/B trên clip tiếng Việt. |
| T08 | Build/regression và hướng dẫn đã xong; chưa đủ nghiệm thu toàn luồng | Release build 0 warning/0 error MSBuild; Vite còn cảnh báo chunk >500 kB. .NET 1076 Passed/0 Failed/5 Skipped; frontend 87 Passed/0 Failed/0 Skipped. Các test opt-in được ghi riêng, không cộng skip thành pass. |

Bằng chứng sau thay đổi: [regression-final.trx](artifacts/test-results/local-voice-deployment-20260910/regression-final.trx), [model cấu hình cuối](artifacts/test-results/local-voice-deployment-20260910/real-model-final.trx), [model kiểm tra lặp](artifacts/test-results/local-voice-deployment-20260910/real-model-repeat.trx). Nhóm LocalVoice trong bộ .NET chuẩn có 43 Passed và 1 Skipped; test bị skip đó đã được chạy riêng bằng model thật ở hai lượt trên. Bốn test opt-in còn chưa chạy thuộc Piper, Qwen integration/benchmark và SQL TikTok. Hướng dẫn: [HUONG_DAN_CHAY_DONG_NHAT_GIONG_VEO_LOCAL.md](HUONG_DAN_CHAY_DONG_NHAT_GIONG_VEO_LOCAL.md). Phiên này không chạy migration, không gọi provider có phí và không dừng server/IDE đang hoạt động.

Kiểm tra sau khi sửa lỗi kết nối Account Server: restore/build Release thành công; [login-launcher-fix.trx](artifacts/test-results/local-voice-deployment-20260910/login-launcher-fix.trx) có 1076 Passed / 0 Failed / 5 Skipped. Launcher khởi động server nền và mở desktop; gọi lại dùng cùng tiến trình, runtime trả READY. HTTPS giữ xác minh chứng chỉ. Chưa thực hiện đăng nhập tài khoản thật hoặc request provider có phí.

Số đo model thật ở lượt đạt của cấu hình cuối:

| Bước | Peak working set của Python model | CPU time của tiến trình |
|---|---:|---:|
| Probe | 814,9 MiB | 3,9 giây |
| Chuẩn bị mẫu | 1.760,3 MiB | 17,9 giây |
| Chuyển giọng | 1.894,1 MiB | 27,7 giây |

Tổng thời gian thực 228,4 giây gồm kiểm checksum đầy đủ trước mỗi worker, probe, tạo media thử, lấy mẫu, chuyển giọng, remux và retry cache. Đây là số đo trên máy hiện tại, không phải cam kết thời gian mỗi clip hoặc tổng RAM toàn ứng dụng.

Lượt kiểm tra lặp dùng workspace test mới và cùng binary/worker: 254,1 giây; peak working set lần lượt 814,0 / 1.774,0 / 1.926,0 MiB cho probe / mẫu / chuyển giọng. CPU time tương ứng 4,6 / 16,4 / 25,6 giây. Hai lượt đều kiểm audio nghe được bằng phép đo, thời lượng MP4 và video packet hash, cùng retry cache giữ nguyên SHA-256; chưa chấm chất lượng tiếng Việt bằng tai.

Trong quá trình kiểm tra đã gặp access violation `0xc0000005` ở tách âm Demucs; diagnostic riêng tái hiện tại `torch._native_multi_head_attention`. Tắt riêng MHA hoặc dùng math attention trên đoạn đầy đủ vẫn có lượt lỗi. Cấu hình cuối dùng một luồng CPU, math attention, tắt oneDNN/MHA fastpath và giới hạn cả `model.segment` lẫn `apply_model` ở 4 giây; model checkpoint/hash giữ nguyên. Đã thêm regression chặn đường attention tối ưu và đo RAM trong tiến trình model. Các lần lỗi được giữ riêng tại [native crash](artifacts/test-results/local-voice-deployment-20260910/real-model-native-crash.trx), [MHA-only](artifacts/test-results/local-voice-deployment-20260910/real-model-mha-only-crash.trx), [đoạn đầy đủ](artifacts/test-results/local-voice-deployment-20260910/real-model-full-segment-crash.trx); chúng không được tính là Passed.

**Điểm xuất phát trước thay đổi**

- [x] Restore và build Release toàn solution đạt; MSBuild báo 0 warning/0 error. Vite còn cảnh báo bundle JavaScript lớn hơn 500 kB.
- [x] Frontend: 87 Passed, 0 Failed, 0 Skipped.
- [x] .NET: 1067 Passed, 0 Failed, 5 Skipped sau khi dùng TEMP/TMP ngắn trên ổ D. Lượt đầu dùng đường dẫn tạm dài gặp 56 lỗi; chạy lại cùng source với đường dẫn ngắn đã đạt.
- [x] Trong tổng .NET, nhóm LocalVoice: 34 Passed, 0 Failed, 1 Skipped. Test bị skip là model thật; các test còn lại xác minh điều phối, quyền, cache, duyệt, tính hiện hành của kết quả và FFmpeg.
- [x] Xác minh đường gọi: React LocalVoicePanel → DashboardBridge.LocalVoice → LocalVoiceService → Python worker → FFmpeg → kiểm tra asset trước render.
- [ ] `Features:VeoLocalVoiceConsistencyEnabled` đang false; workspace chưa có `appsettings.user.json` ghi đè.
- [ ] Component store theo cấu hình hiện tại chưa có Python, model, manifest hoặc READY. Lệnh Python hệ thống chưa khả dụng để chạy bộ test helper riêng.
- [ ] Chưa xác minh server/database đang dùng cho bản triển khai đích trong lần kiểm tra này; chưa chạy chuyển giọng trên clip Veo tiếng Việt thật.

Bằng chứng test mới: [readiness-short-temp.trx](artifacts/test-results/local-voice-readiness-996a8e43/readiness-short-temp.trx). Năm test opt-in bị skip gồm OpenVoice, Piper, Qwen integration, Qwen benchmark và SQL TikTok. Những test đó chưa được tính là đạt. Cloud Fal LipSync và test của nó đang bị loại khỏi build mặc định qua `Directory.Build.targets`; không thuộc phạm vi hoàn thiện lần này.

**Cấu hình đích cần đạt**

| Lớp | Trạng thái đích | Cách đạt |
|---|---|---|
| Desktop | `Features:VeoLocalVoiceConsistencyEnabled=true` | Bật trong cấu hình bản desktop được triển khai từ dự án này; kiểm tra cấu hình người dùng không ghi đè thành false; khởi động bản mới và xác minh trạng thái trả về. |
| Runtime | `READY` | Cài component đã ghim, xác minh checksum, nạp model và probe thành công. READY phải do runtime tạo qua quy trình hợp lệ. |
| Project được chọn | `LocalVoicePolicyVersion=veo-local-voice-v1` | Bật bằng luồng project hiện có sau khi kiểm tra quyền và context. |
| Project đầu vào | `OpenAiStructuredPlan` + `fal` + `ProviderNativeVerified` | Sử dụng project Fal/Veo video dài với Provider Native Audio. |
| Server | Endpoint `/api/generation/local-voice/access` dùng được | Server cùng phiên bản, session/device/license/membership/ownership hợp lệ. Hiện không có cờ server riêng cho chuyển giọng local. |

Canonical Voice, ASR và cloud lip-sync là các luồng độc lập. Tính năng đang triển khai xử lý clip có Native Audio; chọn Canonical Voice sẽ làm project không thỏa điều kiện của panel local. Không đổi policy hàng loạt trên project cũ. Nếu cần tạo thêm clip Veo, đó là yêu cầu provider có phí riêng; ưu tiên dùng clip đã có.

**Task theo thứ tự triển khai**

| ID | Công việc | Điều kiện hoàn thành | Phụ thuộc |
|---|---|---|---|
| T01 | Xác minh môi trường đích: cấu hình server/desktop, instance/database, schema local voice, quyền workflow và project có clip phù hợp. | Ghi nhận đúng môi trường, endpoint truy cập hoạt động; xác minh cột `vf.Projects.LocalVoicePolicyVersion`, constraint và version `4.1.8-local-voice-consistency`. Chốt project, nhân vật và 2–3 cảnh dùng nghiệm thu. | Không |
| T02 | Chuẩn bị runtime/cache/temp trên ổ D và bật cờ desktop cho bản triển khai. | Đường dẫn ngắn, đủ dung lượng, writable; runtime và quá trình cài không đổ dữ liệu lớn vào ổ C. Cờ desktop thực tế là true. Media và workspace hiện có giữ nguyên. | T01 |
| T03 | Cài Python được quản lý, PyTorch CPU, Silero, Demucs và OpenVoice V2 bằng installer hiện có; xử lý lỗi cài nếu phát hiện. | Đúng version/hash, đủ file bắt buộc, manifest đầy đủ; `LocalVoiceRuntime` chạy probe và trả READY. Khởi động lại vẫn đọc đúng runtime đó. | T02 |
| T04 | Chạy test Python bằng interpreter đã cài và test model thật qua C# LocalVoiceRuntime. | Helper tests đạt; model thực hiện được lấy mẫu, chuyển giọng, ghép MP4 và retry từ cache. Ghi riêng thời gian, bộ nhớ và Passed/Failed/Skipped. | T03 |
| T05 | Kiểm tra trên WebView2 thật: hiển thị panel, trạng thái runtime, bật policy cho project đã chọn và chọn mẫu. | Desktop của đúng dự án gửi/nhận được bridge và API; người dùng thấy READY, danh sách cảnh hợp lệ, tiến trình, lỗi và nút hủy. Bật project không làm thay đổi project khác. | T01–T04 |
| T06 | Chạy toàn luồng với clip Veo tiếng Việt thật: mẫu → chuyển giọng 2–3 cảnh cùng nhân vật → nghe A/B → duyệt → render/export. | Người dùng nghe và xác nhận chất lượng; MP4 xuất được, có audio hợp lệ, hình giữ nguyên ở bước chuyển giọng, dùng đúng kết quả đã duyệt. Lưu kết quả kỹ thuật và nhận xét nghe riêng. | T05 |
| T07 | Kiểm tra hủy, chạy lại, mở lại ứng dụng, kết quả hết hiệu lực và giới hạn tài nguyên. | Không mất clip gốc/mẫu/kết quả; không tạo lại request Veo khi retry local; đổi mẫu/nguồn làm kết quả cũ hết hiệu lực; lỗi model/hash/audio được báo rõ. Có số đo CPU/RAM/thời gian cho máy đích. | T06 |
| T08 | Chạy regression cần thiết, hoàn thiện tài liệu và bàn giao bản chạy đúng đường dẫn. | Build/test sau thay đổi đạt; model test local voice đã chạy thật. Ghi các test opt-in còn skip ngoài phạm vi; có hướng dẫn mở ứng dụng, chọn project, chạy chuyển giọng và xuất MP4. | T07 |

**Chi tiết kỹ thuật cần xử lý khi thực hiện task**

- T01: đối chiếu schema bằng truy vấn chỉ đọc trước. Nhật ký cũ ghi nhận đã áp migration trên một môi trường nhưng không thay thế xác minh hiện tại. Nếu thiếu schema, thực hiện theo [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md): xác định môi trường được phép tác động, backup/restore thử, rehearsal và apply idempotent. Không khởi động server mới lên schema chưa phù hợp vì startup có thể bootstrap dữ liệu. Không dừng server/IDE đang phục vụ công việc khác để chạy kiểm tra.
- T02: ổ C hiện còn khoảng 1,5 GB, ổ D khoảng 27,4 GB tại lần kiểm tra. Kiểm tra lại trước khi cài. Đề xuất component root ngắn `D:\VideoMakerLocalVoice\v1` và temp/cache `D:\VideoMakerLocalVoice\tmp`. Bổ sung tùy chọn cấu hình native cho component/temp nếu cần, giữ tương thích mặc định. `LocalVoiceRuntime` đã nhận component root override nhưng đường composition hiện chưa đọc tùy chọn riêng từ cấu hình. Installer/worker cần nhận TEMP/TMP và cache đúng phạm vi tiến trình, giữ kiểm tra path/reparse/hash; không nhận đường dẫn này từ DOM. Không chuyển toàn bộ workspace sang D chỉ để cài model.
- T03: installer ghim Python 3.11.11, PyTorch/torchaudio CPU, OpenVoice V2, Silero VAD và Demucs. Dùng lock/hash hiện có; Python hệ thống không phải điều kiện bắt buộc. Kiểm tra download, thời gian chờ, hủy/cài lại và thông báo lỗi. Giữ license/provenance của component. Inference sử dụng file local và không upload media.
- T04: test opt-in hiện có là `LocalVoiceModelTests.RealModels_AnchorConversionAndRemux_AreAudibleAndPreserveVideo`; cần component root và hai sample được phép sử dụng. Test hiện tạo hình `testsrc2`, nên kết quả của nó chỉ là smoke kỹ thuật. T06 vẫn phải dùng clip Veo thật. Không đặt READY hoặc bỏ kiểm checksum để làm test đạt.
- T05/T06: scene phải Approved/current, thoại trực diện một nhân vật, generation Veo 4/6/8 giây; nhân vật và prompt/asset còn hợp lệ. Mẫu cần ít nhất 1,5 giây lời nói sau xử lý; ngưỡng tối thiểu không bảo đảm mẫu tốt. Người dùng chọn mẫu sạch và thực hiện xác nhận quyền dùng giọng, nghe, duyệt. Không tự đánh dấu các xác nhận đó thay người dùng.
- T06/T07: xác minh nội dung câu nói, dấu tiếng Việt, độ giống mẫu, ngắt nghỉ, khẩu hình, âm nền và SFX bằng nghe/xem. Kiểm thời lượng không thay thế đánh giá này. Source hiện không có diarization tự động; việc scene có một nhân vật trong metadata không chứng minh audio thực tế chỉ có một người nói. Chưa đặt ngưỡng hiệu năng đạt trước khi đo trên máy đích.
- T08: dùng bộ lệnh trong [KIEM_THU_VA_NGHIEM_THU.md](KIEM_THU_VA_NGHIEM_THU.md), TEMP/TMP ngắn trên D. Chạy model thật riêng, không đồng thời với build hoặc tác vụ media nặng. Không dùng kết quả test trước thay đổi làm kết quả cuối. Hướng dẫn hoặc shortcut bàn giao phải trỏ đúng binary vừa build của dự án này; chưa có yêu cầu publish release.

**Định nghĩa hoàn thành cho người dùng**

- [ ] Mở đúng desktop và kết nối được server/schema phù hợp.
- [x] Cờ đồng nhất giọng bật và runtime thực tế READY.
- [ ] Project Fal/Veo được chọn đã bật local policy.
- [ ] Chọn được mẫu giọng từ clip native đã duyệt.
- [ ] Chuyển giọng được các cảnh tiếng Việt thật, nghe A/B và duyệt trong giao diện.
- [ ] Dựng và xuất được MP4 bằng các kết quả đã duyệt còn hiệu lực.
- [ ] Đóng/mở lại, hủy và chạy lại sử dụng được dữ liệu đã có.
- [x] Có kết quả model thật, số đo tài nguyên, các giới hạn còn lại và hướng dẫn sử dụng.

Bật cờ, cài đủ file hoặc qua unit test riêng lẻ chưa đủ để đánh dấu mục tiêu hoàn thành. T06 và các điều kiện trên là điểm nghiệm thu tính năng dùng trên máy này.
