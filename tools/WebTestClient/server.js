// Web 테스트 클라이언트용 개발 서버 (외부 패키지 없음).
//  - public/ 정적 파일 제공
//  - /api/*  → API_URL 로 프록시 (같은 출처이므로 API에 CORS 설정이 필요 없다)
//  - /dev/mail?to=<email> → MAIL_DIR(API의 Email:PickupDirectory)의 테스트 메일 조회
// 사용: API_URL=http://localhost:5080 MAIL_DIR=..\..\.e2e\mail PORT=5090 node server.js
import http from 'node:http';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), 'public');
const API_URL = process.env.API_URL ?? 'http://localhost:5080';
const MAIL_DIR = process.env.MAIL_DIR ?? '';
const PORT = Number(process.env.PORT ?? 5090);
const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8' };

async function readMails(to) {
  if (!MAIL_DIR) return [];
  let files; try { files = (await fs.readdir(MAIL_DIR)).filter(f => f.endsWith('.txt')).sort(); } catch { return []; }
  const out = [];
  for (const f of files) {
    const text = await fs.readFile(path.join(MAIL_DIR, f), 'utf8');
    const [head, ...rest] = text.split('\r\n\r\n');
    const h = Object.fromEntries(head.split('\r\n').map(l => [l.slice(0, l.indexOf(':')).toLowerCase(), l.slice(l.indexOf(':') + 1).trim()]));
    if (!to || h.to?.toLowerCase() === to.toLowerCase()) out.push({ file: f, to: h.to, subject: h.subject, body: rest.join('\r\n\r\n').trim() });
  }
  return out;
}

const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, 'http://localhost');
    if (url.pathname.startsWith('/api/')) {
      const chunks = []; for await (const c of req) chunks.push(c);
      const headers = { ...req.headers }; delete headers.host; delete headers.connection; delete headers['content-length'];
      const r = await fetch(API_URL + url.pathname + url.search, { method: req.method, headers, body: chunks.length ? Buffer.concat(chunks) : undefined });
      const body = Buffer.from(await r.arrayBuffer());
      const out = {}; r.headers.forEach((v, k) => { if (!['content-encoding', 'transfer-encoding', 'connection', 'content-length'].includes(k)) out[k] = v; });
      res.writeHead(r.status, out).end(body);
      return;
    }
    if (url.pathname === '/dev/mail') {
      res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' }).end(JSON.stringify(await readMails(url.searchParams.get('to'))));
      return;
    }
    if (url.pathname === '/dev/config') {
      res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' }).end(JSON.stringify({ apiUrl: API_URL, mail: !!MAIL_DIR }));
      return;
    }
    const file = path.normalize(path.join(root, url.pathname === '/' ? 'index.html' : url.pathname));
    if (!file.startsWith(root)) { res.writeHead(403).end(); return; }
    const data = await fs.readFile(file);
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] ?? 'application/octet-stream' }).end(data);
  } catch (e) {
    const code = e.code === 'ENOENT' ? 404 : 502;
    res.writeHead(code, { 'Content-Type': 'text/plain; charset=utf-8' }).end(code === 404 ? 'Not found' : `Proxy error: ${e.message}`);
  }
});
server.listen(PORT, () => console.log(`Web test client: http://localhost:${PORT}  (API ${API_URL}, mail ${MAIL_DIR || '없음'})`));
