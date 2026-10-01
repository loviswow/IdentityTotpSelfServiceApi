// 헤드리스 Chrome에서 index.html?autorun=1 을 열어 브라우저 안에서 전체 시나리오를 실행하고 결과를 수집한다.
// 외부 패키지 없이 Chrome DevTools Protocol(WebSocket)만 사용한다. 실패가 있으면 exit code 1.
// 사용: node run-browser.mjs --url http://localhost:5090 [--admin email:password] [--chrome <chrome.exe>] [--out log.txt]
import { spawn } from 'node:child_process';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

const arg = (n) => { const i = process.argv.indexOf(n); return i > 0 ? process.argv[i + 1] : undefined; };
const base = arg('--url') ?? 'http://localhost:5090';
const chrome = arg('--chrome') ?? 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const port = 9300 + Math.floor(Math.random() * 500);
const profile = await fs.mkdtemp(path.join(os.tmpdir(), 'totp-e2e-chrome-'));
const target = `${base}/?autorun=1` + (arg('--admin') ? `&admin=${encodeURIComponent(arg('--admin'))}` : '');

const proc = spawn(chrome, ['--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, '--no-first-run', '--no-default-browser-check', '--disable-extensions', 'about:blank'], { stdio: 'ignore' });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let exitCode = 1;
try {
  let page;
  for (let i = 0; i < 50 && !page; i++) {
    try { page = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find((t) => t.type === 'page'); } catch { await sleep(200); }
  }
  if (!page) throw new Error('Chrome DevTools에 연결하지 못했습니다');

  const ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
  let id = 0; const pending = new Map(); const consoleErrors = [];
  ws.onmessage = (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m.result); pending.delete(m.id); }
    if (m.method === 'Runtime.exceptionThrown') consoleErrors.push(m.params.exceptionDetails.exception?.description ?? m.params.exceptionDetails.text);
    if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') consoleErrors.push(m.params.args.map((a) => a.value ?? a.description).join(' '));
  };
  const send = (method, params = {}) => new Promise((res) => { const i = ++id; pending.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  const evaluate = async (expr) => (await send('Runtime.evaluate', { expression: expr, returnByValue: true })).result?.value;

  await send('Runtime.enable');
  await send('Page.enable');
  await send('Page.navigate', { url: target });
  console.log(`Chrome(headless) → ${target.replace(/admin=[^&]+/, 'admin=***')}`);

  let title = '';
  for (let i = 0; i < 240 && !/^(PASS|FAIL) /.test(title ?? ''); i++) { await sleep(500); title = await evaluate('document.title'); }
  const log = await evaluate("document.getElementById('log').innerText");
  console.log(log);
  if (consoleErrors.length) console.log('브라우저 콘솔 오류:\n  ' + consoleErrors.join('\n  '));
  console.log(`페이지 결과: ${title || '시간 초과'}`);
  if (arg('--out')) await fs.writeFile(arg('--out'), log ?? '');
  exitCode = title?.startsWith('PASS ') && consoleErrors.length === 0 ? 0 : 1;
  ws.close();
} catch (e) {
  console.error(e.message);
} finally {
  proc.kill();
  await sleep(300);
  await fs.rm(profile, { recursive: true, force: true }).catch(() => {});
}
process.exit(exitCode);
