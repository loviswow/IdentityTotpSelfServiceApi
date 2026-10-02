// 브라우저와 같은 scenario.js를 Node에서 실행한다. 실패가 있으면 exit code 1.
// 사용: node run-scenario.mjs [--base http://localhost:5090] [--admin email:password:totpKey] [--out result.json]
//   관리자 API는 MFA 세션만 허용하므로 관리자 계정의 TOTP 키(BASE32)를 함께 준다. 비밀번호에는 ":"를 쓰지 않는다.
import fs from 'node:fs/promises';
import { runScenario } from './public/scenario.js';

const arg = (n) => { const i = process.argv.indexOf(n); return i > 0 ? process.argv[i + 1] : undefined; };
const base = arg('--base') ?? 'http://localhost:5090';
const admin = arg('--admin');
const [adminEmail, adminPassword, adminTotpKey] = admin ? admin.split(':') : [];

const r = await runScenario({ base, adminEmail, adminPassword, adminTotpKey });
if (arg('--out')) await fs.writeFile(arg('--out'), JSON.stringify(r, null, 2));
process.exit(r.failed.length ? 1 : 0);
