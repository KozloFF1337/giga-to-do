import { mkdirSync, writeFileSync, copyFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { buildFurnitureCatalog } from '../vendor/pixel-agents/core/src/assets/build.js';
import { decodeAllCharacters, decodeAllFloors, decodeAllWalls, decodeAllCarpets, decodeAllFurniture } from '../vendor/pixel-agents/core/src/assets/loader.js';
const dir = fileURLToPath(new URL('../vendor/pixel-agents/assets/', import.meta.url));
const out = new URL('../public/pixel-agents/', import.meta.url);
mkdirSync(out, { recursive: true });
const catalog = buildFurnitureCatalog(dir);
writeFileSync(new URL('assets.json', out), JSON.stringify({
  characters: decodeAllCharacters(dir), floors: decodeAllFloors(dir),
  walls: decodeAllWalls(dir), carpets: decodeAllCarpets(dir),
  furniture: { catalog, sprites: decodeAllFurniture(dir, catalog) }
}));
copyFileSync(new URL('../vendor/pixel-agents/LICENSE', import.meta.url), new URL('LICENSE.txt', out));
