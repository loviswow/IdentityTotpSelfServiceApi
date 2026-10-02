// Identity TOTP API 전체 시나리오. 브라우저(index.html)와 Node(run-scenario.mjs)에서 같은 코드를 실행한다.
// base: server.js 주소(같은 출처 프록시). /api/* 는 API로, /dev/mail 은 테스트 메일 조회로 전달된다.

// ---------- TOTP (RFC 6238, SHA1, 30초, 6자리) ----------
function base32Decode(s) {
  const a = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = 0, val = 0; const out = [];
  for (const ch of s.replace(/=+$/, '').toUpperCase()) {
    const i = a.indexOf(ch); if (i < 0) continue;
    val = (val << 5) | i; bits += 5;
    if (bits >= 8) { out.push((val >>> (bits - 8)) & 255); bits -= 8; }
  }
  return new Uint8Array(out);
}

export async function totp(sharedKey, now = Date.now()) {
  const key = await crypto.subtle.importKey('raw', base32Decode(sharedKey), { name: 'HMAC', hash: 'SHA-1' }, false, ['sign']);
  const counter = new ArrayBuffer(8);
  new DataView(counter).setUint32(4, Math.floor(now / 1000 / 30));
  const h = new Uint8Array(await crypto.subtle.sign('HMAC', key, counter));
  const o = h[h.length - 1] & 0xf;
  const bin = ((h[o] & 0x7f) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3];
  return String(bin % 1000000).padStart(6, '0');
}

// ---------- HTTP ----------
export function createApi(base) {
  async function call(method, path, body, bearer) {
    const headers = { Accept: 'application/json' };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (bearer) headers.Authorization = `Bearer ${bearer}`;
    const res = await fetch(base + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    const text = await res.text();
    let json = null; try { json = text ? JSON.parse(text) : null; } catch { /* 본문이 JSON이 아님 */ }
    return { status: res.status, json, text };
  }
  return {
    call,
    register: (email, password) => call('POST', '/api/auth/register', { email, password }),
    login: (email, password, deviceName) => call('POST', '/api/auth/login', { email, password, deviceName }),
    twoFactor: (challengeToken, code, deviceName) => call('POST', '/api/auth/2fa', { challengeToken, code, deviceName }),
    recovery: (challengeToken, recoveryCode) => call('POST', '/api/auth/2fa/recovery', { challengeToken, recoveryCode }),
    refresh: (refreshToken) => call('POST', '/api/auth/token/refresh', { refreshToken }),
    revoke: (refreshToken) => call('POST', '/api/auth/token/revoke', { refreshToken }),
    confirmEmail: (userId, token) => call('POST', '/api/auth/confirm-email', { userId, token }),
    resendConfirmation: (email) => call('POST', '/api/auth/resend-confirmation', { email }),
    forgotPassword: (email) => call('POST', '/api/auth/forgot-password', { email }),
    resetPassword: (email, token, newPassword) => call('POST', '/api/auth/reset-password', { email, token, newPassword }),
    me: (at) => call('GET', '/api/account/me', undefined, at),
    changePassword: (at, currentPassword, newPassword) => call('POST', '/api/account/change-password', { currentPassword, newPassword }, at),
    status2fa: (at) => call('GET', '/api/account/2fa/status', undefined, at),
    setup2fa: (at) => call('POST', '/api/account/2fa/setup', undefined, at),
    // 등록 QR 이미지. 본문 대신 상태·Content-Type·캐시 헤더만 확인한다.
    qr2fa: async (at, format = 'png') => { const r = await fetch(base + '/api/account/2fa/qr?format=' + format, { headers: { Authorization: `Bearer ${at}` } }); return { status: r.status, type: r.headers.get('content-type'), cache: r.headers.get('cache-control'), size: (await r.arrayBuffer()).byteLength }; },
    enable2fa: (at, code) => call('POST', '/api/account/2fa/enable', { code }, at),
    disable2fa: (at, password, code) => call('POST', '/api/account/2fa/disable', { password, code }, at),
    reset2fa: (at, password, code) => call('POST', '/api/account/2fa/reset', { password, code }, at),
    regenerateCodes: (at, code) => call('POST', '/api/account/2fa/recovery-codes/regenerate', { code }, at),
    sessions: (at) => call('GET', '/api/account/sessions', undefined, at),
    revokeSession: (at, sessionId) => call('DELETE', `/api/account/sessions/${encodeURIComponent(sessionId)}`, undefined, at),
    revokeOtherSessions: (at) => call('POST', '/api/account/sessions/revoke-others', undefined, at),
    revokeAllSessions: (at) => call('POST', '/api/account/sessions/revoke-all', undefined, at),
    adminReset2fa: (at, userId, reason) => call('POST', '/api/admin/users/2fa/reset', { userId, reason }, at),
    adminSessions: (at, userId) => call('GET', `/api/admin/users/${encodeURIComponent(userId)}/sessions`, undefined, at),
    adminRevokeAllSessions: (at, userId, reason) => call('POST', '/api/admin/users/sessions/revoke-all', { userId, reason }, at),
    adminAuditLogs: (at, query) => call('GET', '/api/admin/audit-logs?' + new URLSearchParams(query), undefined, at),
    // 테스트 메일(PickupDirectory) 조회. server.js 전용.
    mails: async (to) => (await call('GET', '/dev/mail?to=' + encodeURIComponent(to))).json ?? [],
  };
}

// ---------- 시나리오 ----------
class StepError extends Error {}

export async function runScenario({ base, adminEmail, adminPassword, adminTotpKey, log = console.log }) {
  const api = createApi(base);
  const results = [];
  const id = Math.random().toString(16).slice(2, 10);
  const email = `web-${id}@e2e.local`;
  let pw = 'Strong!Pass123';
  const s = {}; // 시나리오 상태(토큰 등)

  const expect = (res, status, what) => {
    if (res.status !== status) throw new StepError(`${what}: 기대 ${status}, 실제 ${res.status} ${res.text?.slice(0, 200) ?? ''}`);
    return res.json;
  };
  const check = (cond, what) => { if (!cond) throw new StepError(what); };
  async function step(name, fn) {
    const t0 = Date.now();
    try { await fn(); results.push({ name, ok: true }); log(`PASS  ${name} (${Date.now() - t0}ms)`); }
    catch (e) { results.push({ name, ok: false, error: e.message }); log(`FAIL  ${name}: ${e.message}`); }
  }
  const skip = (name, why) => { results.push({ name, ok: true, skipped: true }); log(`SKIP  ${name}: ${why}`); };
  const lastMail = async (to, subject) => {
    for (let i = 0; i < 20; i++) {
      const m = (await api.mails(to)).filter(x => x.subject === subject).pop();
      if (m) return m;
      await new Promise(r => setTimeout(r, 100));
    }
    throw new StepError(`${to} 앞으로 '${subject}' 메일이 없습니다`);
  };
  const confirmInfo = (m) => {
    const r = /Confirmation token: ([^;\s]+); userId: (\S+)/.exec(m.body);
    check(r, '확인 메일 형식이 다릅니다'); return { token: r[1], userId: r[2] };
  };
  const loginTokens = async (password = pw, deviceName) => {
    const j = expect(await api.login(email, password, deviceName), 200, '로그인');
    check(j.requiresTwoFactor === false && j.accessToken && j.refreshToken, '2FA 미사용 로그인은 토큰을 바로 받아야 합니다');
    return j;
  };
  const challenge = async (password = pw) => {
    const j = expect(await api.login(email, password), 200, '로그인');
    check(j.requiresTwoFactor === true && j.challengeToken && !j.accessToken, '2FA 사용자는 challengeToken만 받아야 합니다');
    return j.challengeToken;
  };
  const login2fa = async (password = pw) => expect(await api.twoFactor(await challenge(password), await totp(s.key)), 200, 'TOTP 2차 인증');
  const wrong = async () => ((await totp(s.key)) === '000000' ? '111111' : '000000');

  log(`테스트 사용자: ${email}`);

  // --- 회원가입 / 이메일 확인 ---
  await step('AUTH 회원가입 → 201 + 확인 메일', async () => {
    const j = expect(await api.register(email, pw), 201, '회원가입');
    check(j.emailConfirmationRequired === true, 'emailConfirmationRequired=true 여야 합니다');
    s.userId = confirmInfo(await lastMail(email, 'Confirm email')).userId;
  });
  await step('AUTH 중복 이메일 회원가입 → 400', async () => expect(await api.register(email, pw), 400, '중복 가입'));
  await step('AUTH 약한 비밀번호 회원가입 → 400', async () => expect(await api.register(`weak-${id}@e2e.local`, 'weak'), 400, '약한 비밀번호'));
  await step('AUTH 이메일 미확인 로그인 → 401', async () => expect(await api.login(email, pw), 401, '미확인 로그인'));
  await step('AUTH 확인 메일 재발송 → 202', async () => expect(await api.resendConfirmation(email), 202, '재발송'));
  await step('AUTH 잘못된 확인 토큰 → 400', async () => expect(await api.confirmEmail(s.userId, 'invalid'), 400, '잘못된 토큰'));
  await step('AUTH 이메일 확인 → 204', async () => {
    const c = confirmInfo(await lastMail(email, 'Confirm email'));
    expect(await api.confirmEmail(c.userId, c.token), 204, '이메일 확인');
  });

  // --- 로그인 / 토큰 ---
  await step('AUTH 존재하지 않는 사용자 → 401', async () => expect(await api.login(`none-${id}@e2e.local`, pw), 401, '없는 사용자'));
  await step('AUTH 잘못된 비밀번호 → 401', async () => expect(await api.login(email, 'Wrong!Pass999'), 401, '잘못된 비밀번호'));
  await step('AUTH 정상 로그인 → Access/Refresh', async () => { s.t = await loginTokens(); });
  await step('ACCOUNT 내 정보 /me', async () => {
    const j = expect(await api.me(s.t.accessToken), 200, '/me');
    check(j.email === email && j.twoFactorEnabled === false, `/me 응답 불일치: ${JSON.stringify(j)}`);
  });
  await step('ACCOUNT 토큰 없이 /me → 401', async () => expect(await api.me(null), 401, '/me 무인증'));
  await step('2FA 상태 조회 (비활성)', async () => {
    const j = expect(await api.status2fa(s.t.accessToken), 200, 'status');
    check(j.isTwoFactorEnabled === false, '2FA가 꺼져 있어야 합니다');
  });
  await step('TOKEN Refresh 회전 → 새 토큰', async () => {
    const j = expect(await api.refresh(s.t.refreshToken), 200, 'refresh');
    check(j.refreshToken && j.refreshToken !== s.t.refreshToken && j.accessToken && j.accessTokenExpiresAt, '새 Access/Refresh 토큰이어야 합니다');
    s.oldRt = s.t.refreshToken; s.t = { ...s.t, accessToken: j.accessToken, refreshToken: j.refreshToken };
  });
  await step('TOKEN 회전된 Refresh 재사용 → 401', async () => expect(await api.refresh(s.oldRt), 401, '재사용'));
  await step('TOKEN 재사용 탐지 후 최신 Refresh도 폐기 → 401', async () => expect(await api.refresh(s.t.refreshToken), 401, 'family revoke'));
  await step('TOKEN 잘못된 Refresh → 401', async () => expect(await api.refresh('not-a-token'), 401, '잘못된 토큰'));
  await step('TOKEN 로그아웃(revoke) → 204, 이후 Refresh 401', async () => {
    const t = await loginTokens();
    expect(await api.revoke(t.refreshToken), 204, 'revoke');
    expect(await api.refresh(t.refreshToken), 401, 'revoke 후 refresh');
  });

  // --- TOTP 등록 ---
  await step('2FA setup → sharedKey/otpauth URI', async () => {
    s.t = await loginTokens();
    const j = expect(await api.setup2fa(s.t.accessToken), 200, 'setup');
    check(j.sharedKey && j.authenticatorUri?.startsWith('otpauth://totp/'), 'sharedKey/authenticatorUri가 필요합니다');
    s.key = j.sharedKey;
  });
  await step('2FA 등록 QR 이미지(PNG·BMP) → 200, 캐시 금지', async () => {
    for (const [fmt, type] of [['png', 'image/png'], ['bmp', 'image/bmp']]) {
      const r = await api.qr2fa(s.t.accessToken, fmt);
      check(r.status === 200 && r.type === type && r.size > 100, `${fmt}: ${JSON.stringify(r)}`);
      check((r.cache ?? '').includes('no-store'), `캐시 금지 헤더가 없습니다: ${r.cache}`);
    }
  });
  await step('2FA enable 잘못된 코드 → 400', async () => expect(await api.enable2fa(s.t.accessToken, await wrong()), 400, 'enable 오류'));
  await step('2FA enable 정상 → 복구 코드 10개', async () => {
    const j = expect(await api.enable2fa(s.t.accessToken, await totp(s.key)), 200, 'enable');
    check(j.isTwoFactorEnabled === true && j.recoveryCodes?.length === 10, '복구 코드 10개가 필요합니다');
    s.codes = j.recoveryCodes;
  });
  await step('2FA enable 후 기존 Access → 401 (SecurityStamp)', async () => expect(await api.me(s.t.accessToken), 401, '기존 access'));
  await step('2FA enable 후 기존 Refresh → 401 (REG-007)', async () => expect(await api.refresh(s.t.refreshToken), 401, '기존 refresh'));
  await step('2FA 로그인 → challengeToken만 발급', async () => { s.ch = await challenge(); });
  await step('2FA 잘못된 TOTP → 401', async () => expect(await api.twoFactor(s.ch, await wrong()), 401, '잘못된 TOTP'));
  await step('2FA 위조 challengeToken → 401', async () => expect(await api.twoFactor('forged.token.value', await totp(s.key)), 401, '위조 challenge'));
  await step('2FA 정상 TOTP → Access/Refresh', async () => {
    const j = expect(await api.twoFactor(s.ch, await totp(s.key)), 200, 'TOTP');
    check(j.accessToken && j.refreshToken, '토큰이 필요합니다'); s.t = j;
  });
  await step('2FA challengeToken은 Access Token으로 쓸 수 없음 → 401', async () => expect(await api.me(await challenge()), 401, 'challenge를 bearer로'));
  await step('2FA 상태 조회 (활성, 복구 코드 10)', async () => {
    const j = expect(await api.status2fa(s.t.accessToken), 200, 'status');
    check(j.isTwoFactorEnabled === true && j.recoveryCodesLeft === 10, JSON.stringify(j));
  });
  await step('2FA 이미 활성 상태에서 setup → 409', async () => expect(await api.setup2fa(s.t.accessToken), 409, '중복 setup'));
  await step('2FA 복구 코드 재발급 잘못된 코드 → 401', async () => expect(await api.regenerateCodes(s.t.accessToken, await wrong()), 401, 'regenerate 오류'));
  await step('2FA 복구 코드 재발급 → 새 코드 10개, 기존 코드 무효', async () => {
    const j = expect(await api.regenerateCodes(s.t.accessToken, await totp(s.key)), 200, 'regenerate');
    check(j.recoveryCodes?.length === 10, '복구 코드 10개'); s.oldCodes = s.codes; s.codes = j.recoveryCodes;
    expect(await api.recovery(await challenge(), s.oldCodes[0]), 401, '이전 복구 코드');
  });
  await step('2FA 복구 코드 로그인 → 200, 같은 코드 재사용 → 401', async () => {
    expect(await api.recovery(await challenge(), s.codes[0]), 200, '복구 코드');
    expect(await api.recovery(await challenge(), s.codes[0]), 401, '복구 코드 재사용');
  });

  // --- 비밀번호 변경 ---
  await step('PWD 비밀번호 변경 → 기존 Access/Refresh/비밀번호 무효', async () => {
    const t = await login2fa();
    const npw = 'Changed!Pass456';
    expect(await api.changePassword(t.accessToken, 'Wrong!Pass999', npw), 400, '현재 비밀번호 오류');
    expect(await api.changePassword(t.accessToken, pw, npw), 204, '비밀번호 변경');
    expect(await api.me(t.accessToken), 401, '기존 access');
    expect(await api.refresh(t.refreshToken), 401, '기존 refresh (REG-004)');
    expect(await api.login(email, pw), 401, '기존 비밀번호');
    pw = npw;
    s.t = await login2fa();
  });

  // --- 2FA 초기화 / 비활성화 ---
  await step('2FA reset 잘못된 비밀번호 → 401', async () => expect(await api.reset2fa(s.t.accessToken, 'Wrong!Pass999', await totp(s.key)), 401, 'reset 오류'));
  await step('2FA reset → 2FA 해제, 기존 Refresh 401 (REG-005)', async () => {
    expect(await api.reset2fa(s.t.accessToken, pw, await totp(s.key)), 200, 'reset');
    expect(await api.refresh(s.t.refreshToken), 401, 'reset 후 refresh');
    s.t = await loginTokens(); // 2FA가 꺼졌으므로 바로 토큰
  });
  await step('2FA 재등록 후 disable → 204, 기존 Refresh 401 (REG-005)', async () => {
    s.key = expect(await api.setup2fa(s.t.accessToken), 200, 'setup').sharedKey;
    expect(await api.enable2fa(s.t.accessToken, await totp(s.key)), 200, 'enable');
    const t = await login2fa();
    expect(await api.disable2fa(t.accessToken, pw, await wrong()), 401, 'disable 잘못된 코드');
    expect(await api.disable2fa(t.accessToken, pw, await totp(s.key)), 204, 'disable');
    expect(await api.refresh(t.refreshToken), 401, 'disable 후 refresh');
    s.t = await loginTokens();
  });
  await step('2FA 비활성 상태에서 disable → 400', async () => expect(await api.disable2fa(s.t.accessToken, pw, '123456'), 400, 'disable 중복'));

  // --- 비밀번호 분실 / 재설정 ---
  await step('PWD forgot-password 없는 이메일도 202 (계정 존재 숨김)', async () => expect(await api.forgotPassword(`none-${id}@e2e.local`), 202, 'forgot 없는 이메일'));
  await step('PWD 잘못된 재설정 토큰 → 400', async () => expect(await api.resetPassword(email, 'invalid', 'Reset!Pass789'), 400, '잘못된 토큰'));
  await step('PWD forgot → 메일 토큰으로 reset → 새 비밀번호 로그인, 기존 Refresh 401', async () => {
    const before = s.t;
    expect(await api.forgotPassword(email), 202, 'forgot');
    const m = await lastMail(email, 'Password reset');
    const token = /Reset token: (\S+)/.exec(m.body)?.[1]; check(token, '재설정 메일 형식');
    const npw = 'Reset!Pass789';
    expect(await api.resetPassword(email, token, npw), 204, 'reset-password');
    expect(await api.refresh(before.refreshToken), 401, '재설정 후 refresh');
    expect(await api.me(before.accessToken), 401, '재설정 후 access');
    pw = npw; s.t = await loginTokens();
  });

  // --- 세션(기기) 관리 ---
  const sessionOf = (list, deviceName) => list.find(x => x.deviceName === deviceName);
  await step('SESSION 기기 이름으로 로그인 → 세션 목록에 표시, 현재 세션 표시', async () => {
    s.pc = await loginTokens(pw, 'E2E-PC'); s.phone = await loginTokens(pw, 'E2E-Phone');
    const list = expect(await api.sessions(s.pc.accessToken), 200, '세션 목록');
    const pc = sessionOf(list, 'E2E-PC'), phone = sessionOf(list, 'E2E-Phone');
    check(pc && phone, `두 기기가 목록에 있어야 합니다: ${JSON.stringify(list)}`);
    check(pc.current === true && phone.current === false, '현재 세션 표시가 맞지 않습니다');
    check(!JSON.stringify(list).includes(s.pc.refreshToken), '세션 목록에 Refresh Token이 노출되었습니다');
    s.phoneSession = phone.sessionId;
  });
  await step('SESSION 특정 기기 로그아웃 → 그 기기 Access/Refresh 401, 다른 기기 유지', async () => {
    expect(await api.revokeSession(s.pc.accessToken, s.phoneSession), 204, '세션 폐기');
    expect(await api.me(s.phone.accessToken), 401, '폐기한 기기 access');
    expect(await api.refresh(s.phone.refreshToken), 401, '폐기한 기기 refresh');
    expect(await api.me(s.pc.accessToken), 200, '현재 기기 access');
    expect(await api.revokeSession(s.pc.accessToken, 'no-such-session'), 404, '없는 세션');
  });
  await step('SESSION 다른 기기 모두 로그아웃 → 현재 기기만 남음', async () => {
    const tablet = await loginTokens(pw, 'E2E-Tablet');
    const j = expect(await api.revokeOtherSessions(s.pc.accessToken), 200, 'revoke-others');
    check(j.revokedSessions >= 1, `revokedSessions=${j.revokedSessions}`);
    expect(await api.me(tablet.accessToken), 401, '다른 기기 access');
    expect(await api.refresh(tablet.refreshToken), 401, '다른 기기 refresh');
    const list = expect(await api.sessions(s.pc.accessToken), 200, '세션 목록');
    check(list.length === 1 && list[0].current === true, `현재 세션만 남아야 합니다: ${JSON.stringify(list)}`);
  });
  await step('SESSION 로그아웃한 세션의 Access Token → 401', async () => {
    expect(await api.revoke(s.pc.refreshToken), 204, '로그아웃');
    expect(await api.me(s.pc.accessToken), 401, '로그아웃 후 access');
  });
  await step('SESSION 전체 기기 로그아웃 → 모든 Access/Refresh 401', async () => {
    const a = await loginTokens(pw, 'E2E-A'), b = await loginTokens(pw, 'E2E-B');
    expect(await api.revokeAllSessions(a.accessToken), 204, 'revoke-all');
    for (const t of [a, b]) {
      expect(await api.me(t.accessToken), 401, '전체 로그아웃 후 access');
      expect(await api.refresh(t.refreshToken), 401, '전체 로그아웃 후 refresh');
    }
    s.t = await loginTokens();
  });

  // --- 관리자 (Admin 역할 + MFA 세션 필수) ---
  await step('ADMIN 일반 사용자 호출 → 403', async () => expect(await api.adminReset2fa(s.t.accessToken, s.userId, 'test'), 403, '일반 사용자'));
  if (adminEmail && adminPassword && adminTotpKey) {
    await step('ADMIN 관리자 TOTP 로그인 → MFA 세션', async () => {
      const l = expect(await api.login(adminEmail, adminPassword, 'E2E-Admin'), 200, '관리자 로그인');
      check(l.requiresTwoFactor === true, '관리자는 2FA가 켜져 있어야 합니다(관리자 API는 MFA 세션 필수)');
      s.admin = expect(await api.twoFactor(l.challengeToken, await totp(adminTotpKey), 'E2E-Admin'), 200, '관리자 TOTP');
    });
    await step('ADMIN 사유 없음 → 400, 없는 사용자 → 404, 정상 → 204', async () => {
      s.key = expect(await api.setup2fa(s.t.accessToken), 200, 'setup').sharedKey;
      expect(await api.enable2fa(s.t.accessToken, await totp(s.key)), 200, 'enable');
      const target = await login2fa();
      const at = s.admin.accessToken;
      expect(await api.adminReset2fa(at, s.userId, ''), 400, '사유 없음');
      expect(await api.adminReset2fa(at, 'no-such-user', 'test'), 404, '없는 사용자');
      expect(await api.adminReset2fa(at, s.userId, 'E2E 기기 분실'), 204, '관리자 초기화');
      expect(await api.refresh(target.refreshToken), 401, '관리자 초기화 후 refresh');
      expect(await api.me(target.accessToken), 401, '관리자 초기화 후 access');
      s.t = await loginTokens(); // 2FA 해제됨
    });
    await step('ADMIN 감사 로그 조회 → 초기화 기록과 수행자(ActorUserId)', async () => {
      const items = expect(await api.adminAuditLogs(s.admin.accessToken, { userId: s.userId, eventType: 'admin.2fa.reset', limit: 5 }), 200, '감사 로그');
      check(items.some(x => x.detail === 'E2E 기기 분실' && x.actorUserId && x.actorUserId !== s.userId), `초기화 기록이 없습니다: ${JSON.stringify(items)}`);
      expect(await api.adminAuditLogs(s.admin.accessToken, { limit: 1000 }), 400, 'limit 초과');
    });
    await step('ADMIN 대상 세션 조회 → 전체 로그아웃 → 대상 토큰 401', async () => {
      const t = await loginTokens(pw, 'E2E-Target');
      const list = expect(await api.adminSessions(s.admin.accessToken, s.userId), 200, '대상 세션 목록');
      check(sessionOf(list, 'E2E-Target'), `대상 세션이 없습니다: ${JSON.stringify(list)}`);
      expect(await api.adminRevokeAllSessions(s.admin.accessToken, s.userId, ''), 400, '사유 없음');
      expect(await api.adminRevokeAllSessions(s.admin.accessToken, s.userId, 'E2E 침해 대응'), 204, '전체 로그아웃');
      expect(await api.me(t.accessToken), 401, '대상 access');
      expect(await api.refresh(t.refreshToken), 401, '대상 refresh');
      s.t = await loginTokens();
    });
  } else skip('ADMIN 관리자 API', '관리자 계정(email:password:totpKey)이 주어지지 않음');

  // --- 계정 잠금 (마지막: 계정이 잠긴다) ---
  await step('AUTH 비밀번호 5회 실패 → 정상 비밀번호도 423', async () => {
    for (let i = 0; i < 5; i++) await api.login(email, 'Wrong!Pass999');
    expect(await api.login(email, pw), 423, '잠금');
  });

  const failed = results.filter(r => !r.ok);
  log(`\n결과: ${results.length - failed.length}/${results.length} 통과` + (failed.length ? `, 실패 ${failed.length}` : ''));
  return { email, results, failed };
}
