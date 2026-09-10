import { useEffect, useRef, useState } from 'react';
import { Link2, Plus, Settings2, X } from 'lucide-react';
import type { useTikTokModule } from './useTikTokModule';
import type { TikTokConnection } from './types';
import { formatTikTokFailure } from './tiktokFailure';

type Module = ReturnType<typeof useTikTokModule>;
const statusLabel = (account: TikTokConnection) => account.status === 'Disconnected' ? 'Đã ngắt kết nối'
  : account.status === 'ReconnectRequired' ? 'Cần kết nối lại' : 'Đã kết nối';
const name = (account: TikTokConnection) => account.creatorUsername ? '@' + account.creatorUsername : account.creatorNickname || 'Tài khoản TikTok';

export function TikTokAccounts({ module }: { module: Module }) {
  const { state } = module;
  const accounts = state.feature.connections ?? (state.feature.connection ? [state.feature.connection] : []);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [disconnecting, setDisconnecting] = useState<TikTokConnection | null>(null);
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    if (open && dialog.current && !dialog.current.open) dialog.current.showModal();
    return () => { if (dialog.current?.open) dialog.current.close(); };
  }, [open]);
  const close = () => { setDisconnecting(null); setOpen(false); };
  const selected = state.feature.connection;
  const available = state.feature.enabled && state.feature.configured;
  const canAdd = available && (accounts.length === 0 || state.feature.multiAccountEnabled === true);
  const matches = accounts.filter(account => (account.creatorUsername + ' ' + account.creatorNickname).toLocaleLowerCase('vi').includes(query.trim().toLocaleLowerCase('vi')));
  return <>
    <section className="tiktok-account-bar tiktok-multi-account-bar">
      <AccountAvatar account={selected} />
      <label className="tiktok-account-choice"><span>Đăng đến tài khoản</span>
        <select aria-label="Tài khoản nhận bài đăng" value={state.selectedConnectionId ?? selected?.connectionId ?? ''}
          disabled={state.busy} onChange={event => module.selectAccount(event.target.value)}>
          <option value="">Chọn tài khoản TikTok</option>
          {accounts.map(account => <option key={account.connectionId} value={account.connectionId}>
            {name(account)}{account.status && account.status !== 'Connected' ? ' · ' + statusLabel(account) : ''}
          </option>)}
        </select>
      </label>
      <button className="ghost-button" type="button" disabled={state.busy} onClick={() => setOpen(true)}><Settings2 size={16} /> Quản lý ({accounts.length})</button>
      {canAdd && <button className="start-button" type="button" disabled={state.busy} onClick={() => module.connect()}><Plus size={16} /> Thêm tài khoản</button>}
    </section>
    <dialog ref={dialog} className="tiktok-accounts-dialog" aria-labelledby="tiktokAccountsTitle" onCancel={close}>
      <header><div><h2 id="tiktokAccountsTitle">Tài khoản TikTok</h2><p>Mỗi bài đăng được gửi đến tài khoản bạn chọn.</p></div>
        <button className="icon-button" type="button" onClick={close} aria-label="Đóng quản lý tài khoản"><X size={20} /></button></header>
      {disconnecting ? <section className="tiktok-disconnect-confirm">
        <h3>Ngắt kết nối {name(disconnecting)}?</h3>
        <p>Lịch sử đăng vẫn được giữ. Video đã gửi có thể tiếp tục được TikTok xử lý.</p>
        <p>Có {state.jobs.filter(job => job.connectionId === disconnecting.connectionId && !job.isTerminal).length} bài sẽ dừng theo dõi nếu bạn ngắt kết nối.</p>
        <div className="tiktok-account-actions"><button className="ghost-button" onClick={() => setDisconnecting(null)}>Quay lại</button>
          <button className="danger-outline" disabled={state.busy} onClick={() => { module.disconnect(disconnecting.connectionId); setDisconnecting(null); }}>Ngắt tài khoản này</button></div>
      </section> : <>
        <input className="tiktok-account-search" aria-label="Tìm tài khoản TikTok" placeholder="Tìm tên hoặc username…" value={query} onChange={event => setQuery(event.target.value)} />
        <div className="tiktok-account-list">
          {matches.map(account => <article key={account.connectionId} className="tiktok-account-row">
            <AccountAvatar account={account} />
            <div className="tiktok-account-row-identity"><strong>{account.creatorNickname || name(account)}</strong><span>{name(account)}</span><small>{statusLabel(account)}</small></div>
            <div className="tiktok-account-actions">
              <button className="ghost-button" disabled={state.busy} onClick={() => { module.selectAccount(account.connectionId); close(); }}>Chọn</button>
              <button className="ghost-button" disabled={state.busy || !available} onClick={() => module.connect(account.connectionId)}><Link2 size={14} /> Kết nối lại</button>
              {account.status !== 'Disconnected' && <button className="danger-outline" disabled={state.busy} onClick={() => setDisconnecting(account)}>Ngắt</button>}
            </div>
          </article>)}
          {!matches.length && <p>Không có tài khoản phù hợp.</p>}
        </div>
        <footer>{canAdd ? <button className="start-button" disabled={state.busy} onClick={() => module.connect()}><Plus size={16} /> Thêm tài khoản TikTok</button>
          : <p>{available ? 'Việc thêm nhiều tài khoản chưa được mở. Bạn vẫn có thể sử dụng hoặc kết nối lại tài khoản hiện có.' : 'Tính năng đăng đang tạm dừng trên server. Bạn vẫn có thể ngắt tài khoản và xem lịch sử.'}</p>}
          <small>Hãy chọn đúng tài khoản trên trang đăng nhập TikTok trong trình duyệt.</small></footer>
      </>}
      {state.error && <p role="alert" className="field-error">{state.error}</p>}
    </dialog>
  </>;
}

function AccountAvatar({ account }: { account?: TikTokConnection | null }) {
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [account?.avatarUrl]);
  const valid = account?.avatarUrl?.startsWith('https://tiktok-media.app.local/avatar/');
  return <div className="tiktok-account-avatar" aria-hidden="true">{valid && !failed
    ? <img src={account!.avatarUrl!} alt="" onError={() => setFailed(true)} />
    : (account?.creatorNickname || account?.creatorUsername || '♪').slice(0, 1).toLocaleUpperCase('vi')}</div>;
}

export function TikTokHistory({ module }: { module: Module }) {
  const { state } = module;
  const running = state.jobs.filter(job => !job.isTerminal);
  const history = state.history;
  const accounts = state.feature.connections ?? (state.feature.connection ? [state.feature.connection] : []);
  return <section className="tiktok-history">
    {state.feature.unresolvedAttempts?.filter(a => !state.selectedConnectionId || a.connectionId === state.selectedConnectionId).map(attempt =>
      <p key={attempt.clientRequestId} className="field-error">Lần đăng {new Date(attempt.createdAtUtc).toLocaleString('vi-VN')}: {attempt.status === 'Rejected'
        ? 'TikTok đã từ chối khởi tạo.' : 'Chưa xác định được kết quả khởi tạo. Hãy kiểm tra trên TikTok trước khi tạo bài mới.'}</p>)}
    {running.length > 0 && <div className="tiktok-running-jobs" aria-label="Các bài đang xử lý">
      {running.map(job => <button key={job.publishJobId} disabled={state.busy} onClick={() => module.showJob(job)}>
        @{job.creatorUsername || state.feature.connections?.find(a => a.connectionId === job.connectionId)?.creatorUsername || 'TikTok'}
        <span>{job.status === 'PROCESSING_UPLOAD' ? 'Đang tải video' : 'TikTok đang xử lý'}</span>
      </button>)}
    </div>}
    <details onToggle={event => { if (event.currentTarget.open) module.loadHistory(); }}>
      <summary>Lịch sử đăng {state.feature.connection?.creatorUsername ? '· @' + state.feature.connection.creatorUsername : '· Tất cả tài khoản'}</summary>
      <div className="tiktok-history-heading"><span>{history?.totalCount ?? 0} bài đăng</span>
        <button className="ghost-button" disabled={state.historyLoading} onClick={() => module.loadHistory(history?.page ?? 1)}>Làm mới lịch sử</button></div>
      {state.historyLoading ? <p role="status">Đang tải lịch sử…</p> : !history?.items.length ? <p>Chưa có bài đăng.</p>
        : <ol className="tiktok-history-list">{history.items.map(job => {
          const snapshotName = job.creatorUsername?.trim() ? '@' + job.creatorUsername.trim() : job.creatorNickname?.trim();
          const account = accounts.find(candidate => candidate.connectionId === job.connectionId);
          const currentName = account?.creatorUsername?.trim() ? '@' + account.creatorUsername.trim() : account?.creatorNickname?.trim();
          return <li key={job.publishJobId}>
            <div><strong>{snapshotName || currentName || 'Không xác định được tài khoản'}</strong>
              {!snapshotName && <small>{currentName
                ? 'Đang hiển thị tên hiện tại; bài đăng chưa lưu tên tài khoản lúc đăng.'
                : 'Bài đăng này chưa lưu tên tài khoản lúc đăng.'}</small>}
              <small>{new Date(job.createdAtUtc ?? job.updatedAtUtc).toLocaleString('vi-VN')}</small></div>
            <div><span>{job.status === 'PUBLISH_COMPLETE' ? 'Đã đăng' : job.status === 'FAILED' ? 'Đã dừng' : 'Đang xử lý'}</span>
              {job.failureReason && <small>{formatTikTokFailure(job.failureReason)}</small>}
              {job.publicPostIds.length > 0 && <small>Mã bài: {job.publicPostIds.join(', ')}</small>}</div>
          </li>;
        })}</ol>}
      {history && history.totalCount > history.pageSize && <nav className="tiktok-history-pagination" aria-label="Phân trang lịch sử">
        <button disabled={state.historyLoading || history.page <= 1} onClick={() => module.loadHistory(history.page - 1)}>Trước</button>
        <span>Trang {history.page} / {Math.ceil(history.totalCount / history.pageSize)}</span>
        <button disabled={state.historyLoading || history.page * history.pageSize >= history.totalCount} onClick={() => module.loadHistory(history.page + 1)}>Sau</button>
      </nav>}
    </details>
  </section>;
}
