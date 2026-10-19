import { request, FullConfig } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

async function warmUp(api: Awaited<ReturnType<typeof request.newContext>>) {
  for (const url of ['/', '/grid-lab', '/skills', '/projects', '/planner']) await api.get(url, { timeout: 240_000 });
}

export default async function globalSetup(config: FullConfig) {
  const baseURL = config.projects[0].use.baseURL!;
  const api = await request.newContext({ baseURL });
  const skills = await api.get('/api/v1/skills', { timeout: 240_000 });
  if (skills.ok() && (await skills.json()).length > 0) { await warmUp(api); await api.dispose(); return; }
  const file = path.join(__dirname, 'fixtures', 'agility.xlsx');
  const res = await api.post('/api/v1/imports/agility?commit=true', {
    timeout: 240_000,
    multipart: {
      file: { name: 'agility.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: fs.readFileSync(file) },
    },
  });
  if (!res.ok()) throw new Error(`Seeding the fixture failed: ${res.status()} ${await res.text()}`);
  console.log('e2e database seeded from fixtures/agility.xlsx');
  await warmUp(api);
  await api.dispose();
}
