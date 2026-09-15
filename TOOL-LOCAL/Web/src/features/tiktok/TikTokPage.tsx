import { useEffect, useState, type ReactNode } from 'react';
import { Link2, LoaderCircle, Send, ShieldCheck, Upload, X } from 'lucide-react';
import type { TikTokPublishPayload } from './types';
import type { useTikTokModule } from './useTikTokModule';
import { describeTikTokUnavailable } from './tiktokAvailability';
import { TikTokVideoPreview } from './TikTokVideoPreview';
import { TikTokPublishDialog } from './TikTokPublishDialog';
import { TikTokAccounts, TikTokHistory } from './TikTokAccounts';

type TikTokModule = ReturnType<typeof useTikTokModule>;

export function TikTokPage({ module }: { module: TikTokModule }) {
  const { state } = module;
  useEffect(() => {
    module.refresh();
  }, []);
  const [form, setForm] = useState<TikTokPublishPayload>({
    title: '',
    privacyLevel: '',
    allowComment: false,
    allowDuet: false,
    allowStitch: false,
    commercialContent: false,
    brandContent: false,
    brandOrganic: false,
    isAiGenerated: false,
    consentConfirmed: false
  });

  useEffect(() => {
    setForm((current) => ({
      ...current,
      privacyLevel: '',
      allowComment: false,
      allowDuet: false,
      allowStitch: false,
      commercialContent: false,
      brandContent: false,
      brandOrganic: false,
      consentConfirmed: false
    }));
  }, [state.creator, state.selectedConnectionId]);

  useEffect(() => {
    if (state.feature.connection && !state.selectedConnectionId) module.refreshCreator();
  }, [state.feature.connection?.connectionId]);

  if (state.loading && !state.feature.connection) {
    return <div className="page-shell tiktok-page"><div className="tiktok-loading"><LoaderCircle className="spin" /> Đang tải TikTok...</div></div>;
  }

  if (!state.feature.enabled || !state.feature.configured) {
    const unavailable = describeTikTokUnavailable(state.feature.unavailableReason);
    return (
      <div className="page-shell tiktok-page">
        {Boolean(state.feature.connections?.length) && <TikTokAccounts module={module} />}
        <TikTokPublishDialog state={state} onDismiss={module.hidePublishFeedback} onCancel={module.cancel} />
        <section className="tiktok-empty-card">
          <ShieldCheck size={34} />
          <h2>{unavailable.title}</h2>
          <p>{unavailable.detail}</p>
          {state.error && <ErrorBanner message={state.error} onClose={module.clearError} />}
          <button className="start-button" disabled={state.loading || state.busy} onClick={module.refresh}>Kiểm tra lại</button>
        </section>
        {Boolean(state.feature.connections?.length) && <TikTokHistory module={module} />}
      </div>
    );
  }

  if (!state.feature.connection && !state.feature.connections?.length) {
    return (
      <div className="page-shell tiktok-page">
        <section className="tiktok-empty-card">
          <div className="tiktok-logo">♪</div>
          <h2>{state.feature.isCredentialVerification ? 'Xác minh ứng dụng TikTok' : 'Kết nối tài khoản TikTok'}</h2>
          <p>{state.feature.isCredentialVerification
            ? 'Tài khoản VideoMaker này được phép xác minh. Bấm Kết nối TikTok và cấp quyền trong trình duyệt, sau đó quay lại Admin xem kết quả. Cài đặt Admin sẽ mở sau khi xác minh thành công.'
            : 'VideoMaker sẽ mở trang ủy quyền chính thức trong trình duyệt. Mật khẩu TikTok không đi qua ứng dụng.'}</p>
          {state.error && <ErrorBanner message={state.error} onClose={module.clearError} />}
          <button className="start-button" disabled={state.busy} onClick={() => module.connect()}>
            {state.busy ? <LoaderCircle className="spin" size={18} /> : <Link2 size={18} />}
            Kết nối TikTok
          </button>
        </section>
      </div>
    );
  }

  if (!state.feature.connection || state.feature.connection.status === 'ReconnectRequired' || state.feature.connection.status === 'Disconnected') {
    return <div className="page-shell tiktok-page">
      <TikTokPublishDialog state={state} onDismiss={module.hidePublishFeedback} onCancel={module.cancel} />
      <TikTokAccounts module={module} />
      <section className="tiktok-empty-card"><h2>{state.feature.connection ? 'Kết nối lại tài khoản TikTok' : 'Chọn tài khoản nhận bài đăng'}</h2>
        <p>{state.feature.connection ? 'Tài khoản này cần được cấp quyền lại trước khi đăng video. Các tài khoản khác vẫn hoạt động riêng.' : 'Chọn một tài khoản ở phía trên để chuẩn bị video.'}</p>
        {state.feature.connection && <button className="start-button" disabled={state.busy} onClick={() => module.connect(state.feature.connection!.connectionId)}>Kết nối lại</button>}
        {state.error && <ErrorBanner message={state.error} onClose={module.clearError} />}
      </section><TikTokHistory module={module} />
    </div>;
  }

  const creator = state.creator;
  const mediaTooLong = Boolean(creator && state.media && state.media.durationSeconds > creator.maximumVideoDurationSeconds);
  const commercialValid = !form.commercialContent || form.brandContent || form.brandOrganic;
  const posting = Boolean(state.publish && !state.publish.isTerminal);
  const controlsDisabled = state.busy || posting;
  const canPublish = Boolean(
    creator && !creator.publishingIssue && state.media && form.privacyLevel && form.consentConfirmed && commercialValid && !mediaTooLong && !state.busy &&
    (!state.publish || state.publish.isTerminal)
  );

  return (
    <div className="page-shell tiktok-page">
      <TikTokPublishDialog state={state} onDismiss={module.hidePublishFeedback} onCancel={module.cancel} onNewAttempt={module.newPublishIntent} />
      <TikTokAccounts module={module} />

      {state.error && !state.publishFeedback?.open && <ErrorBanner message={state.error} onClose={module.clearError} />}

      {creator?.publishingIssue && (
        <section className="tiktok-readiness" role="status" aria-labelledby="tiktokReadinessTitle">
          <ShieldCheck size={24} aria-hidden="true" />
          <div>
            <h2 id="tiktokReadinessTitle">Cần chuẩn bị tài khoản trước khi đăng</h2>
            {creator.publishingIssue.code === 'tiktok_private_test_account_required' ? (
              <>
                <p>Kết nối TikTok vẫn hoạt động. Ứng dụng đang thử nghiệm, nên tài khoản nhận video cần đặt ở chế độ riêng tư.</p>
                <ol>
                  <li>Mở TikTok → Cài đặt và quyền riêng tư → Quyền riêng tư.</li>
                  <li>Bật <strong>Tài khoản riêng tư</strong> cho tài khoản nhận video ở trên.</li>
                  <li>Quay lại đây, bấm <strong>Kiểm tra lại</strong>, rồi chọn quyền xem bài đăng <strong>Chỉ mình tôi</strong>.</li>
                </ol>
              </>
            ) : <p>{creator.publishingIssue.message}</p>}
            <button className="start-button" type="button" disabled={state.loading || state.busy} onClick={module.refresh}>
              {state.loading ? 'Đang kiểm tra…' : 'Kiểm tra lại'}
            </button>
          </div>
        </section>
      )}

      <div className="tiktok-grid">
        <section className="tiktok-card tiktok-media-card">
          <header><h2>1. Chọn video</h2><p>File được đọc tại máy và tải trực tiếp lên TikTok.</p></header>
          {state.media ? (
            <>
              <div className="tiktok-preview-stage"><TikTokVideoPreview key={state.media.previewUrl} media={state.media} /></div>
              <div className="tiktok-file-row">
                <div><strong title={state.media.fileName}>{state.media.fileName}</strong><span>{formatBytes(state.media.sizeBytes)} · {formatDuration(state.media.durationSeconds)} · {state.media.width}×{state.media.height} · {state.media.framesPerSecond.toFixed(1)} FPS</span></div>
                <button className="icon-button" disabled={controlsDisabled} onClick={module.clearMedia} title="Bỏ video" aria-label="Bỏ video"><X size={18} /></button>
              </div>
              {mediaTooLong && <p className="field-error">Tài khoản này chỉ cho phép video tối đa {creator?.maximumVideoDurationSeconds} giây.</p>}
            </>
          ) : (
            <button className="tiktok-dropzone" disabled={controlsDisabled} onClick={module.selectMedia}>
              <Upload size={28} />
              <strong>Chọn video từ máy</strong>
              <span>MP4, MOV hoặc WebM · tối đa 4 GB</span>
            </button>
          )}
        </section>

        <section className="tiktok-card tiktok-form-card">
          <header><h2>2. Thiết lập bài đăng</h2><p>TikTok yêu cầu bạn tự chọn quyền riêng tư cho từng bài.</p></header>
          <label className="tiktok-field">
            <span>Caption <small>{form.title.length}/2200</small></span>
            <textarea
              rows={2}
              maxLength={2200}
              value={form.title}
              disabled={controlsDisabled}
              placeholder="Viết caption, hashtag hoặc nhắc đến tài khoản..."
              onChange={(event) => setForm({ ...form, title: event.target.value })}
            />
          </label>
          <div className="tiktok-permissions-row">
          <label className="tiktok-field">
            <span>Ai có thể xem video?</span>
            <select
              value={form.privacyLevel}
              disabled={controlsDisabled || !creator}
              onChange={(event) => setForm({ ...form, privacyLevel: event.target.value })}
            >
              <option value="">Chọn quyền riêng tư</option>
              {creator?.privacyLevelOptions.map((option) => (
                <option key={option} value={option} disabled={form.brandContent && option === 'SELF_ONLY'}>
                  {privacyLabel(option)}
                </option>
              ))}
            </select>
          </label>
          <fieldset className="tiktok-options">
            <legend>Cho phép tương tác</legend>
            <Check label="Bình luận" checked={form.allowComment} disabled={controlsDisabled || creator?.commentDisabled} onChange={(value) => setForm({ ...form, allowComment: value })} />
            <Check label="Duet" checked={form.allowDuet} disabled={controlsDisabled || creator?.duetDisabled} onChange={(value) => setForm({ ...form, allowDuet: value })} />
            <Check label="Stitch" checked={form.allowStitch} disabled={controlsDisabled || creator?.stitchDisabled} onChange={(value) => setForm({ ...form, allowStitch: value })} />
          </fieldset>
          </div>
          <fieldset className="tiktok-options disclosure-options">
            <legend>Khai báo nội dung</legend>
            <Check
              label="Nội dung này quảng bá bản thân, thương hiệu, sản phẩm hoặc dịch vụ"
              checked={form.commercialContent}
              disabled={controlsDisabled}
              onChange={(value) => setForm({ ...form, commercialContent: value, brandOrganic: value ? form.brandOrganic : false, brandContent: value ? form.brandContent : false })}
            />
            {form.commercialContent && (
              <div className="tiktok-disclosure-children">
                <Check label="Thương hiệu của bạn" checked={form.brandOrganic} disabled={controlsDisabled} onChange={(value) => setForm({ ...form, brandOrganic: value })} />
                <Check label="Nội dung có thương hiệu / hợp tác trả phí" checked={form.brandContent} disabled={controlsDisabled || form.privacyLevel === 'SELF_ONLY'} onChange={(value) => setForm({ ...form, brandContent: value })} />
                {!commercialValid && <small className="field-error">Hãy chọn ít nhất một loại nội dung thương mại.</small>}
                {form.privacyLevel === 'SELF_ONLY' && <small className="field-error">Nội dung hợp tác trả phí không thể để ở chế độ Chỉ mình tôi.</small>}
                {(form.brandOrganic || form.brandContent) && <small>Video sẽ được gắn nhãn “{form.brandContent ? 'Hợp tác trả phí' : 'Nội dung quảng bá'}”.</small>}
              </div>
            )}
            <Check label="Nội dung do AI tạo" checked={form.isAiGenerated} disabled={controlsDisabled} onChange={(value) => setForm({ ...form, isAiGenerated: value })} />
          </fieldset>
          <Check
            label={(
              <>
                Bằng việc đăng, tôi đồng ý với{' '}
                {form.brandContent && (
                  <><PolicyButton onClick={() => module.openPolicy('branded')}>Chính sách nội dung có thương hiệu</PolicyButton> và{' '}</>
                )}
                <PolicyButton onClick={() => module.openPolicy('music')}>Xác nhận sử dụng âm nhạc</PolicyButton> của TikTok,
                đồng thời xác nhận có quyền sử dụng video và âm thanh này.
              </>
            )}
            checked={form.consentConfirmed}
            disabled={controlsDisabled}
            onChange={(value) => setForm({ ...form, consentConfirmed: value })}
            prominent
          />
          <div className="tiktok-submit-row">
            {state.publishFeedback && <button className="tiktok-secondary-button" onClick={module.showPublishFeedback}>Xem tiến trình</button>}
            <button className="start-button" disabled={!canPublish} onClick={() => module.publish(form)}>
              {state.busy ? <LoaderCircle className="spin" size={18} /> : <Send size={18} />}
              Đăng lên TikTok
            </button>
          </div>
        </section>
      </div>
      <TikTokHistory module={module} />
    </div>
  );
}

function Check({ label, checked, disabled, onChange, prominent = false }: { label: ReactNode; checked: boolean; disabled?: boolean; onChange: (value: boolean) => void; prominent?: boolean }) {
  return <label className={`tiktok-check ${prominent ? 'prominent' : ''}`}><input type="checkbox" checked={checked} disabled={disabled} onChange={(event) => onChange(event.target.checked)} /><span>{label}</span></label>;
}

function PolicyButton({ children, onClick }: { children: ReactNode; onClick: () => void }) {
  return <button type="button" className="tiktok-policy-link" onClick={(event) => { event.preventDefault(); event.stopPropagation(); onClick(); }}>{children}</button>;
}

function ErrorBanner({ message, onClose }: { message: string; onClose: () => void }) {
  return <div className="tiktok-error"><span>{message}</span><button onClick={onClose} aria-label="Đóng"><X size={16} /></button></div>;
}

function privacyLabel(value: string): string {
  return ({ PUBLIC_TO_EVERYONE: 'Mọi người', FOLLOWER_OF_CREATOR: 'Người theo dõi', MUTUAL_FOLLOW_FRIENDS: 'Bạn bè', SELF_ONLY: 'Chỉ mình tôi' } as Record<string, string>)[value] ?? value;
}

function formatBytes(bytes: number): string {
  if (bytes >= 1024 ** 3) return `${(bytes / 1024 ** 3).toFixed(2)} GB`;
  if (bytes >= 1024 ** 2) return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

function formatDuration(seconds: number): string {
  const total = Math.round(seconds);
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`;
}
