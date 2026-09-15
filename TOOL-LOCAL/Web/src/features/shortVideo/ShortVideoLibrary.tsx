import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { Check, ChevronLeft, ChevronRight, ImagePlus, Images, MoreHorizontal, Plus, Search, Shirt, UserRound, X } from 'lucide-react';
import type { ShortVideoAssetKind, ShortVideoAssetRef, ShortVideoLibraryAsset, ShortVideoLibraryUpload } from '../../types';

export function ShortDialog({ title, children, onClose, busy = false, wide = false }: { title: string; children: ReactNode; onClose: () => void; busy?: boolean; wide?: boolean }) {
  const dialog = useRef<HTMLDialogElement>(null); const titleId = useId();
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    dialog.current?.showModal();
    return () => { dialog.current?.close(); previous?.focus(); };
  }, []);
  return <dialog ref={dialog} className={`sv-dialog${wide ? ' sv-dialog-wide' : ''}`} aria-labelledby={titleId} onCancel={event => { event.preventDefault(); if (!busy) onClose(); }}>
    <header><h2 id={titleId}>{title}</h2><button className="sv-icon-button" aria-label="Đóng cửa sổ" disabled={busy} onClick={onClose}><X size={19} /></button></header>
    {children}
  </dialog>;
}

const label = (kind: ShortVideoAssetKind) => kind === 'Character' ? 'nhân vật' : 'trang phục';
const matches = (a: ShortVideoAssetRef | null | undefined, b: ShortVideoAssetRef) => a?.assetId === b.assetId && a.version === b.version;
export function navigateShortVideoTabs(event: KeyboardEvent<HTMLDivElement>) {
  if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
  const tabs = [...event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="tab"]')].filter(tab => !tab.disabled);
  const current = tabs.indexOf(document.activeElement as HTMLButtonElement);
  if (!tabs.length || current < 0) return;
  const next = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : (current + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
  event.preventDefault(); tabs[next].focus(); tabs[next].click();
}

function AssetCard({ asset, selected, onSelect, onManage, disabled }: { asset: ShortVideoLibraryAsset; selected: boolean; onSelect: () => void; onManage: () => void; disabled: boolean }) {
  return <div className={`sv-asset${selected ? ' is-selected' : ''}`}>
    <button className="sv-asset-select" title={asset.name} aria-label={`Chọn ${label(asset.kind)}: ${asset.name}`} aria-pressed={selected} disabled={disabled} onClick={onSelect}>
      <span className={`sv-thumbnail ${asset.kind === 'Outfit' ? 'sv-clothing' : ''}`}><img src={asset.thumbnailUrl} alt="" loading="lazy" onError={event => event.currentTarget.classList.add('sv-image-error')} />{selected && <span className="sv-selected-check"><Check size={12} strokeWidth={3} /></span>}</span>
      <span className="sv-asset-name">{asset.name}</span>
    </button>
    <button className="sv-asset-menu" aria-label={`Quản lý ${asset.name}`} disabled={disabled} onClick={onManage}><MoreHorizontal size={16} /></button>
  </div>;
}

function AssetStrip({ kind, items, selected, currentPreview, disabled, onAdd, onSelect, onManage, onAll, onImportCurrent }: {
  kind: ShortVideoAssetKind; items: ShortVideoLibraryAsset[]; selected: ShortVideoLibraryAsset | null; currentPreview?: string | null; disabled: boolean;
  onAdd: () => void; onSelect: (item: ShortVideoLibraryAsset) => void; onManage: (item: ShortVideoLibraryAsset) => void; onAll: () => void; onImportCurrent: () => void;
}) {
  const strip = useRef<HTMLDivElement>(null); const Icon = kind === 'Character' ? UserRound : Shirt;
  const [overflow, setOverflow] = useState(false);
  useEffect(() => {
    const element = strip.current;
    if (!element) return;
    const measure = () => setOverflow(element.scrollWidth > element.clientWidth + 1);
    measure();
    if (typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(measure); observer.observe(element);
    return () => observer.disconnect();
  }, [items.length, selected]);
  const visible = items.slice(0, 20);
  if (selected && !visible.some(item => matches(item, selected))) visible.unshift(selected);
  return <section className="sv-library-strip" aria-label={`Thư viện ${label(kind)}`}>
    <header><Icon size={22} /><div><h3>{kind === 'Character' ? 'Nhân vật' : 'Trang phục'}</h3><p>{kind === 'Character' ? 'Chọn một nhân vật từ thư viện của bạn' : 'Chọn một bộ trang phục cho nhân vật'}</p></div>
      {items.length > 0 && <button className="sv-strip-all" aria-label={`Xem tất cả ${label(kind)} (${items.length})`} title={`Xem tất cả (${items.length})`} onClick={onAll} disabled={disabled}><Images size={14} />{items.length}</button>}
      <button className="sv-add-button" disabled={disabled} onClick={onAdd}><Plus size={15} />Thêm {label(kind)}</button></header>
    <div className="sv-strip-body">
      <div className="sv-strip-scroll" ref={strip}>
        <button className="sv-upload-tile" disabled={disabled} aria-label={`Chọn ảnh ${label(kind)}`} onClick={onAdd}><ImagePlus size={23} /><span>Tải ảnh lên</span></button>
        {visible.map(item => <AssetCard key={`${item.assetId}/${item.version}`} asset={item} selected={matches(selected, item)} disabled={disabled} onSelect={() => onSelect(item)} onManage={() => onManage(item)} />)}
        {!selected && currentPreview && <div className="sv-asset is-selected"><button className="sv-asset-select" onClick={onImportCurrent} disabled={disabled} title="Lưu ảnh đang dùng vào thư viện">
          <span className={`sv-thumbnail ${kind === 'Outfit' ? 'sv-clothing' : ''}`}><img src={currentPreview} alt={`Ảnh ${label(kind)} đang dùng`} /><span className="sv-selected-check"><Check size={12} /></span></span><span className="sv-asset-name">Ảnh đang dùng</span></button></div>}
        {visible.length === 0 && !currentPreview && <div className="sv-library-empty"><strong>Thư viện {label(kind)} của bạn</strong><span>Thêm ảnh để chọn và dùng lại cho những video sau.</span></div>}
      </div>
      {overflow && <div className="sv-strip-arrows"><button className="sv-icon-button" aria-label={`Cuộn ${label(kind)} sang trái`} onClick={() => strip.current?.scrollBy({ left: -330, behavior: 'smooth' })}><ChevronLeft size={15} /></button><button className="sv-icon-button" aria-label={`Cuộn ${label(kind)} sang phải`} onClick={() => strip.current?.scrollBy({ left: 330, behavior: 'smooth' })}><ChevronRight size={15} /></button></div>}
    </div>
  </section>;
}

type LibraryProps = {
  available: boolean;
  items: ShortVideoLibraryAsset[]; selectedCharacter: ShortVideoLibraryAsset | null; selectedOutfit: ShortVideoLibraryAsset | null;
  characterPreview?: string | null; outfitPreview?: string | null; disabled: boolean;
  request: <T>(type: string, payload?: object) => Promise<T>; onChanged: (item?: ShortVideoLibraryAsset, removedId?: string) => void; onSelect: (item: ShortVideoLibraryAsset) => void;
};

export function ShortVideoLibrary(props: LibraryProps) {
  const [panel, setPanel] = useState<{ kind: ShortVideoAssetKind; mode: 'add' | 'manage' | 'detail'; asset?: ShortVideoLibraryAsset; upload?: ShortVideoLibraryUpload } | null>(null);
  const [loading, setLoading] = useState(false); const [error, setError] = useState('');
  const [upload, setUpload] = useState<ShortVideoLibraryUpload | null>(null); const [name, setName] = useState('');
  const [search, setSearch] = useState(''); const [sort, setSort] = useState('recent'); const [page, setPage] = useState(1); const [confirmDelete, setConfirmDelete] = useState(false);
  useEffect(() => { if (!props.available) setPanel(null); }, [props.available]);
  const locked = props.disabled || loading;
  function open(kind: ShortVideoAssetKind, mode: 'add' | 'manage' | 'detail', asset?: ShortVideoLibraryAsset) {
    setPanel({ kind, mode, asset }); setUpload(null); setName(asset?.name ?? ''); setError(''); setSearch(''); setPage(1); setConfirmDelete(false);
  }
  async function execute(work: () => Promise<void>) {
    setLoading(true); setError('');
    try { await work(); } catch (e) { if ((e as Error).name !== 'AbortError') setError((e as Error).message); } finally { setLoading(false); }
  }
  function pick() { void execute(async () => {
    const picked = await props.request<ShortVideoLibraryUpload | null>('short-library.pick');
    if (picked) { setUpload(picked); if (!panel?.asset) setName(picked.suggestedName); }
  }); }
  function save(select: boolean) { void execute(async () => {
    if (!panel || !upload) return;
    const item = await props.request<ShortVideoLibraryAsset>('short-library.commit', { kind: panel.kind, name: name.trim(), uploadId: upload.uploadId, assetId: panel.asset?.assetId, version: panel.asset?.version });
    props.onChanged(item); if (select) props.onSelect(item); setPanel(null);
  }); }
  function importCurrent(kind: ShortVideoAssetKind) { open(kind, 'add'); void execute(async () => {
    const picked = await props.request<ShortVideoLibraryUpload>('short-library.import-current', { kind }); setUpload(picked); setName(picked.suggestedName);
  }); }
  const items = props.items.filter(item => item.kind === panel?.kind && item.name.toLocaleLowerCase('vi').includes(search.toLocaleLowerCase('vi')));
  if (sort === 'name') items.sort((a, b) => a.name.localeCompare(b.name, 'vi'));
  if (sort === 'new') items.sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc));
  const selected = panel?.kind === 'Character' ? props.selectedCharacter : props.selectedOutfit;
  return <>
    {(['Character', 'Outfit'] as const).map(kind => <AssetStrip key={kind} kind={kind} items={props.items.filter(item => item.kind === kind)} selected={kind === 'Character' ? props.selectedCharacter : props.selectedOutfit}
      currentPreview={kind === 'Character' ? props.characterPreview : props.outfitPreview} disabled={locked} onAdd={() => open(kind, 'add')} onSelect={props.onSelect} onManage={item => open(kind, 'detail', item)} onAll={() => open(kind, 'manage')} onImportCurrent={() => importCurrent(kind)} />)}
    {panel && <ShortDialog title={panel.mode === 'manage' ? 'Thư viện của bạn' : panel.mode === 'detail' ? `Quản lý ${label(panel.kind)}` : `${panel.asset ? 'Thay ảnh' : 'Thêm'} ${label(panel.kind)}`} wide={panel.mode === 'manage'} busy={loading} onClose={() => setPanel(null)}>
      <div className="sv-dialog-body">
        {panel.mode === 'manage' ? <>
          <div className="sv-tabs" role="tablist" aria-label="Loại tài sản" onKeyDown={navigateShortVideoTabs}>{(['Character', 'Outfit'] as const).map(kind => <button role="tab" tabIndex={panel.kind === kind ? 0 : -1} aria-selected={panel.kind === kind} key={kind} onClick={() => open(kind, 'manage')}>{kind === 'Character' ? 'Nhân vật' : 'Trang phục'} ({props.items.filter(item => item.kind === kind).length})</button>)}</div>
          <div className="sv-library-toolbar"><label className="sv-search"><Search size={16} /><input aria-label="Tìm ảnh theo tên" placeholder="Tìm theo tên…" value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} /></label>
            <select aria-label="Sắp xếp thư viện" value={sort} onChange={event => setSort(event.target.value)}><option value="recent">Dùng gần đây</option><option value="new">Mới thêm</option><option value="name">Tên A–Z</option></select><button onClick={() => open(panel.kind, 'add')}><Plus size={16} />Thêm ảnh</button></div>
          <div className="sv-library-grid">{items.slice(0, page * 24).map(item => <AssetCard key={item.assetId} asset={item} selected={matches(selected, item)} disabled={locked} onSelect={() => { props.onSelect(item); setPanel(null); }} onManage={() => open(item.kind, 'detail', item)} />)}</div>
          {items.length === 0 && <div className="sv-empty"><Images size={32} /><strong>{search ? 'Không tìm thấy ảnh phù hợp' : 'Chưa có ảnh trong thư viện'}</strong><span>{search ? 'Thử một tên khác.' : 'Thêm ảnh đầu tiên để bắt đầu.'}</span></div>}
          {items.length > page * 24 && <button className="sv-load-more" onClick={() => setPage(page + 1)}>Xem thêm</button>}
        </> : <>
          <p className="sv-help">{panel.mode === 'detail' ? 'Đổi tên giữ nguyên ảnh. Thay ảnh tạo phiên bản mới; dự án cũ vẫn giữ ảnh đã chọn.' : 'Lưu ảnh trên máy này để dùng lại trong các dự án của cùng tài khoản và tổ chức.'}</p>
          {(upload || panel.asset) ? <div className="sv-upload-preview"><img src={upload?.previewUrl ?? panel.asset!.previewUrl} alt={`Ảnh ${label(panel.kind)} xem trước`} /><span>{(upload?.image ?? panel.asset!.image).width} × {(upload?.image ?? panel.asset!.image).height} px · {((upload?.image ?? panel.asset!.image).sizeBytes / 1024 / 1024).toFixed(2)} MiB</span></div>
            : <button className="sv-pick-large" disabled={locked} onClick={pick}><ImagePlus size={35} /><strong>Chọn ảnh {label(panel.kind)}</strong><span>PNG / JPEG · Tối đa 10 MiB, 16 megapixel</span></button>}
          {panel.mode === 'add' && upload && <button disabled={locked} onClick={pick}>Chọn ảnh khác</button>}
          <label className="sv-name-field">Tên {label(panel.kind)}<input autoFocus value={name} maxLength={80} disabled={locked} placeholder={panel.kind === 'Character' ? 'Ví dụ: Nhân vật chính' : 'Ví dụ: Áo dài xanh'} onChange={event => setName(event.target.value)} /><span>{name.length}/80</span></label>
          {panel.mode === 'detail' && <div className="sv-manage-actions"><button disabled={locked} onClick={() => open(panel.kind, 'add', panel.asset)}>Thay ảnh</button><button className="sv-danger-button" disabled={locked} onClick={() => setConfirmDelete(true)}>Xóa khỏi thư viện</button></div>}
          {confirmDelete && <div className="sv-warning" role="alert"><p>Xóa “{panel.asset?.name}” khỏi thư viện? Ảnh trong dự án đã lưu vẫn được giữ nguyên.</p><button className="sv-danger-button" disabled={locked} onClick={() => void execute(async () => { await props.request('short-library.delete', { assetId: panel.asset!.assetId, version: panel.asset!.version }); props.onChanged(undefined, panel.asset!.assetId); setPanel(null); })}>Xác nhận xóa khỏi thư viện</button><button disabled={locked} onClick={() => setConfirmDelete(false)}>Giữ lại</button></div>}
        </>}
        {loading && <p role="status" className="sv-help">Đang xử lý ảnh…</p>}{error && <p role="alert" className="sv-error">{error}</p>}
      </div>
      <footer><button disabled={loading} onClick={() => setPanel(null)}>Đóng</button>
        {panel.mode === 'add' && <><button disabled={locked || !upload || !name.trim()} onClick={() => save(false)}>Lưu vào thư viện</button><button className="sv-primary" disabled={locked || !upload || !name.trim()} onClick={() => save(true)}>Lưu và chọn</button></>}
        {panel.mode === 'detail' && <><button disabled={locked || !name.trim() || name.trim() === panel.asset?.name} onClick={() => void execute(async () => { const asset = await props.request<ShortVideoLibraryAsset>('short-library.rename', { assetId: panel.asset!.assetId, version: panel.asset!.version, name }); props.onChanged(asset); setPanel(null); })}>Lưu tên</button><button className="sv-primary" disabled={locked} onClick={() => { props.onSelect(panel.asset!); setPanel(null); }}>Sử dụng ảnh này</button></>}
      </footer>
    </ShortDialog>}
  </>;
}
