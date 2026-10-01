// 브라우저와 같은 scenario.js를 Node에서 실행한다. 실패가 있으면 exit code 1.
// 사용: node run-scenario.mjs [--base http://localhost:5090] [--admin email:password] [--out result.json]
import fs from 'node:fs/promises';
import { runScenario } from './public/scenario.js';

const arg = (n) => { const i = process.argv.indexOf(n); return i > 0 ? process.argv[i + 1] : undefined; };
const base = arg('--base') ?? 'http://localhost:5090';
const admin = arg('--admin');
const [adminEmail, adminPassword] = admin ? [admin.slice(0, admin.indexOf(':')), admin.slice(admin.indexOf(':') + 1)] : [];

const r = await runScenario({ base, adminEmail, adminPassword });
if (arg('--out')) await fs.writeFile(arg('--out'), JSON.stringify(r, null, 2));
process.exit(r.failed.length ? 1 : 0);
