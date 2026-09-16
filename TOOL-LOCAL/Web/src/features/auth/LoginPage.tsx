import { useEffect, useRef, useState, type CSSProperties } from 'react';
import { ArrowRight, Eye, EyeOff, LockKeyhole, Mail, ShieldCheck } from 'lucide-react';
import { isHosted, postToHost, subscribeToHost } from '../../bridge';

export type LoginViewState = {
  busy: boolean;
  status: string;
  error: boolean;
  emailError?: string;
  passwordError?: string;
  focusField?: 'email' | 'password';
};

const initialState: LoginViewState = {
  busy: true,
  status: 'Đang kiểm tra phiên đăng nhập...',
  error: false
};

const designWidth = 600;
const designHeight = 800;

function fitLoginToViewport() {
  const width = Math.min(window.innerWidth, window.visualViewport?.width ?? window.innerWidth);
  const height = Math.min(window.innerHeight, window.visualViewport?.height ?? window.innerHeight);
  return Math.min(1, width / designWidth, height / designHeight);
}

export function LoginPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(true);
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [view, setView] = useState<LoginViewState>(initialState);
  const [fitScale, setFitScale] = useState(fitLoginToViewport);
  const emailRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const updateFit = () => setFitScale(fitLoginToViewport());
    window.addEventListener('resize', updateFit);
    window.visualViewport?.addEventListener('resize', updateFit);
    return () => {
      window.removeEventListener('resize', updateFit);
      window.visualViewport?.removeEventListener('resize', updateFit);
    };
  }, []);

  useEffect(() => {
    const unsubscribe = subscribeToHost(message => {
      if (message.type !== 'auth.state' || !message.payload) return;
      const next = message.payload as LoginViewState;
      setView(next);
      if (next.focusField) {
        window.setTimeout(() => {
          (next.focusField === 'email' ? emailRef : passwordRef).current?.focus();
        }, 0);
      }
    });
    if (isHosted) postToHost('auth.ready');
    else setView({ busy: false, status: 'Mở trong ứng dụng taphoatool để đăng nhập.', error: true });
    return unsubscribe;
  }, []);

  const setFieldValue = (field: 'email' | 'password', value: string) => {
    if (field === 'email') {
      setEmail(value);
      if (view.emailError) setView(current => ({ ...current, emailError: undefined }));
    } else {
      setPassword(value);
      if (view.passwordError) setView(current => ({ ...current, passwordError: undefined }));
    }
  };

  const submit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (view.busy || !isHosted) return;
    const trimmedEmail = email.trim();
    const emailError = !trimmedEmail ? 'Vui lòng nhập email.'
      : !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail) ? 'Email không đúng định dạng.' : undefined;
    const passwordError = !password ? 'Vui lòng nhập mật khẩu.' : undefined;
    if (emailError || passwordError) {
      setView({ busy: false, status: 'Vui lòng kiểm tra lại thông tin đăng nhập.', error: true, emailError, passwordError });
      window.setTimeout(() => (emailError ? emailRef : passwordRef).current?.focus(), 0);
      return;
    }
    setView({ busy: true, status: 'Đang đăng nhập...', error: false });
    postToHost('auth.login', { email: trimmedEmail, password, rememberMe });
  };

  const request = (type: string, payload?: unknown) => {
    if (view.busy || !isHosted) return;
    postToHost(type, payload);
  };

  return (
    <div className="login-viewport" style={{ '--login-fit': fitScale } as CSSProperties}>
    <div className="login-canvas">
      <div className="login-decoration login-decoration--top-left" aria-hidden="true" />
      <div className="login-decoration login-decoration--right" aria-hidden="true" />
      <div className="login-decoration login-decoration--bottom-left" aria-hidden="true" />
      <div className="login-decoration login-decoration--bottom-right" aria-hidden="true" />
      <div className="login-outline" aria-hidden="true" />
      <div className="login-dots login-dots--left" aria-hidden="true" />
      <div className="login-dots login-dots--top-right" aria-hidden="true" />
      <div className="login-dots login-dots--bottom-right" aria-hidden="true" />

      <header className="login-brand">
        <span className="login-brand-symbol" aria-hidden="true" />
        <strong>taphoatool</strong>
        <span className="login-brand-slogan">Tự động tạo video bằng AI chỉ với vài bước</span>
      </header>

      <main className="login-card">
        <h1>Đăng nhập</h1>
        <p className="login-subtitle">Chào mừng bạn trở lại!</p>
        <form onSubmit={submit} noValidate>
          <div className="login-field login-field--email">
            <label htmlFor="login-email">Email</label>
            <div className={`login-input ${view.emailError ? 'login-input--error' : ''}`}>
              <Mail size={18} strokeWidth={1.5} aria-hidden="true" />
              <input ref={emailRef} id="login-email" type="email" autoComplete="username" maxLength={320}
                value={email} onChange={event => setFieldValue('email', event.target.value)}
                placeholder="Nhập email của bạn" disabled={view.busy} aria-invalid={Boolean(view.emailError)}
                aria-describedby={view.emailError ? 'login-email-error' : undefined} />
            </div>
            {view.emailError && <span id="login-email-error" className="login-field-error">{view.emailError}</span>}
          </div>

          <div className="login-field login-field--password">
            <label htmlFor="login-password">Mật khẩu</label>
            <div className={`login-input ${view.passwordError ? 'login-input--error' : ''}`}>
              <LockKeyhole size={18} strokeWidth={1.5} aria-hidden="true" />
              <input ref={passwordRef} id="login-password" type={passwordVisible ? 'text' : 'password'}
                autoComplete="current-password" value={password}
                onChange={event => setFieldValue('password', event.target.value)}
                placeholder="Nhập mật khẩu" disabled={view.busy} aria-invalid={Boolean(view.passwordError)}
                aria-describedby={view.passwordError ? 'login-password-error' : undefined} />
              <button type="button" className="login-eye" disabled={view.busy}
                aria-label={passwordVisible ? 'Ẩn mật khẩu' : 'Hiện mật khẩu'}
                aria-pressed={passwordVisible} onClick={() => setPasswordVisible(value => !value)}>
                {passwordVisible ? <EyeOff size={17} strokeWidth={1.5} /> : <Eye size={17} strokeWidth={1.5} />}
              </button>
            </div>
            {view.passwordError && <span id="login-password-error" className="login-field-error">{view.passwordError}</span>}
          </div>

          <div className="login-remember-row">
            <label><input type="checkbox" checked={rememberMe} disabled={view.busy}
              onChange={event => setRememberMe(event.target.checked)} /> Ghi nhớ đăng nhập</label>
            <button type="button" className="login-text-link" disabled={view.busy}
              onClick={() => request('auth.password-reset.open', { email: email.trim() })}>Quên mật khẩu?</button>
          </div>

          <button className="login-submit" type="submit" disabled={view.busy || !isHosted}>
            <ArrowRight size={18} strokeWidth={2.5} aria-hidden="true" />
            {view.busy ? 'Đang xử lý...' : 'Đăng nhập'}
          </button>
        </form>

        <p className={`login-status ${view.error ? 'login-status--error' : ''}`} role={view.error ? 'alert' : 'status'}
          aria-live={view.error ? 'assertive' : 'polite'}>{view.status}</p>

        <div className="login-divider"><span>Hoặc đăng nhập với</span></div>
        <button type="button" className="login-social login-social--google" disabled={view.busy}
          onClick={() => request('auth.social.unavailable', { provider: 'Google' })}>
          <span aria-hidden="true">G</span>Đăng nhập với Google
        </button>
        <button type="button" className="login-social login-social--facebook" disabled={view.busy}
          onClick={() => request('auth.social.unavailable', { provider: 'Facebook' })}>
          <span aria-hidden="true">f</span>Đăng nhập với Facebook
        </button>
        <div className="login-register">Chưa có tài khoản?
          <button type="button" className="login-text-link" disabled={view.busy}
            onClick={() => request('auth.register.open')}>Đăng ký ngay</button>
        </div>
      </main>

      <footer className="login-security-footer"><ShieldCheck size={17} strokeWidth={1.4} aria-hidden="true" />
        <span>Thông tin của bạn được bảo mật an toàn.</span></footer>
    </div>
    </div>
  );
}
