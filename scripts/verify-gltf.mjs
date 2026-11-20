// Validates glTF/GLB files with the official Khronos glTF-Validator.
// Usage (from any folder where `npm install gltf-validator` was run): node <repo>/scripts/verify-gltf.mjs model.glb [...]
import { createRequire } from 'module';
import fs from 'fs';
const validator = createRequire(process.cwd() + '/')('gltf-validator');
let failed = 0;
for (const f of process.argv.slice(2)) {
  const r = await validator.validateBytes(new Uint8Array(fs.readFileSync(f)), { uri: f, externalResourceFunction: () => Promise.reject(new Error('external resources are not used')) });
  console.log(`${f}: ${r.issues.numErrors} errors, ${r.issues.numWarnings} warnings, ${r.info?.totalTriangleCount ?? 0} triangles`);
  for (const m of r.issues.messages.filter(m => m.severity <= 1)) console.log(`  ${m.code} ${m.message} ${m.pointer ?? ''}`);
  if (r.issues.numErrors > 0) failed++;
}
process.exit(failed ? 1 : 0);
