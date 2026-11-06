import { OfficeState } from '../vendor/pixel-agents/webview-ui/src/office/engine/officeState.js';
import { startGameLoop } from '../vendor/pixel-agents/webview-ui/src/office/engine/gameLoop.js';
import { renderFrame } from '../vendor/pixel-agents/webview-ui/src/office/engine/renderer.js';
import { buildDynamicCatalog } from '../vendor/pixel-agents/webview-ui/src/office/layout/furnitureCatalog.js';
import { setCharacterTemplates } from '../vendor/pixel-agents/webview-ui/src/office/sprites/spriteData.js';
import { setFloorSprites } from '../vendor/pixel-agents/webview-ui/src/office/floorTiles.js';
import { setWallSprites } from '../vendor/pixel-agents/webview-ui/src/office/wallTiles.js';
import { setCarpetSprites } from '../vendor/pixel-agents/webview-ui/src/office/sprites/carpetTiles.js';
import { setProviderCapabilities } from '../vendor/pixel-agents/webview-ui/src/office/toolUtils.js';
import { CharacterState, TILE_SIZE, TileType } from '../vendor/pixel-agents/webview-ui/src/office/types.js';
import type { OfficeLayout, PlacedFurniture } from '../vendor/pixel-agents/webview-ui/src/office/types.js';
import { agentStatus, roles } from './office-model.js';

export interface OfficeAgent {
  id: string; taskId: string; title: string; boardId: string; role: keyof typeof roles;
  state: string; runner: string; needsAttention: boolean; waitingForReview: boolean; slot: number;
}
let loading: Promise<void> | null = null;
export function loadPixelAssets(): Promise<void> {
  if (!loading) loading = fetch('/pixel-agents/assets.json').then(async response => {
    if (!response.ok) throw new Error(`Графика офиса недоступна (${response.status})`);
    const assets = await response.json();
    if (!assets.characters?.length || !assets.furniture?.catalog?.length) throw new Error('Неполный набор графики Pixel Agents');
    buildDynamicCatalog(assets.furniture); setCharacterTemplates(assets.characters);
    setFloorSprites(assets.floors); setWallSprites(assets.walls); setCarpetSprites(assets.carpets);
    setProviderCapabilities({ readingTools: ['Review'], subagentToolNames: [] });
  }).catch(error => { loading = null; throw error; });
  return loading;
}
export function createOfficeLayout(): OfficeLayout {
  const cols = 34, rows = 26;
  const tiles: OfficeLayout['tiles'] = [], tileColors: OfficeLayout['tileColors'] = [];
  for (let row = 0; row < rows; row++) for (let col = 0; col < cols; col++) {
    const wall = row === 0 || row === rows - 1 || col === 0 || col === cols - 1;
    tiles.push(wall ? TileType.WALL : TileType.FLOOR_2);
    tileColors.push(wall ? { h: 100, s: 15, b: -20, c: 0 } : { h: 38, s: 22, b: 5, c: 0 });
  }
  const furniture: PlacedFurniture[] = [];
  for (let i = 0; i < 20; i++) {
    const col = 3 + (i % 5) * 6, row = 4 + Math.floor(i / 5) * 5;
    furniture.push({ uid: `desk-${i}`, type: 'DESK_FRONT', col, row },
      { uid: `pc-${i}`, type: 'PC_FRONT_OFF', col: col + 1, row: row - 1 },
      { uid: `seat-${i}`, type: 'CUSHIONED_CHAIR_BACK', col: col + 1, row: row + 2 });
  }
  furniture.push({ uid: 'board', type: 'WHITEBOARD', col: 14, row: 1 },
    { uid: 'plant-left', type: 'LARGE_PLANT', col: 1, row: 1 },
    { uid: 'plant-right', type: 'LARGE_PLANT', col: 30, row: 1 },
    { uid: 'shelf', type: 'DOUBLE_BOOKSHELF', col: 6, row: 1 },
    { uid: 'sofa', type: 'SOFA_FRONT', col: 3, row: 23 },
    { uid: 'coffee', type: 'COFFEE_TABLE', col: 28, row: 23 });
  return { version: 1, layoutRevision: 1, cols, rows, tiles, tileColors, furniture, pets: [] };
}
export function mountOffice(canvas: HTMLCanvasElement, selectTask: (id: string) => void) {
  const office = new OfficeState(createOfficeLayout());
  const ids = new Map<string, number>();
  let sequence = 1, current: OfficeAgent[] = [], connected = true, zoom = 1, hydrated = false;
  let offset = { offsetX: 0, offsetY: 0 };
  const resize = new ResizeObserver(() => {
    const rect = canvas.getBoundingClientRect(), ratio = window.devicePixelRatio || 1;
    canvas.width = Math.max(1, Math.round(rect.width * ratio)); canvas.height = Math.max(1, Math.round(rect.height * ratio));
  });
  resize.observe(canvas);
  const stop = startGameLoop(canvas, {
    update(dt) { if (connected) office.update(dt); },
    render(ctx) {
      zoom = Math.max(0.1, Math.min(canvas.width / (36 * TILE_SIZE), canvas.height / (28 * TILE_SIZE)));
      offset = renderFrame(ctx, canvas.width, canvas.height, office.tileMap, office.furniture, office.getCharacters(), zoom, 0, 0,
        { selectedAgentId: office.selectedAgentId, hoveredAgentId: null, hoveredTile: null, seats: office.seats, characters: office.characters },
        undefined, office.layout.tileColors, office.layout.cols, office.layout.rows);
      const ratio = window.devicePixelRatio || 1;
      ctx.font = `${Math.max(8, Math.min(11, zoom * 5)) * ratio}px system-ui`;
      ctx.textAlign = 'center'; ctx.textBaseline = 'top';
      for (const agent of current) {
        const ch = office.characters.get(ids.get(agent.id)!); if (!ch) continue;
        const x = offset.offsetX + ch.x * zoom, y = offset.offsetY + (ch.y + 5) * zoom;
        const label = `${roles[agent.role]} · ${agent.taskId.slice(0, 4)}`;
        const width = ctx.measureText(label).width;
        ctx.fillStyle = '#25372fe8'; ctx.fillRect(x - width / 2 - 4 * ratio, y - ratio, width + 8 * ratio, 14 * ratio);
        ctx.fillStyle = '#f4f6ed'; ctx.fillText(label, x, y);
      }
    }
  });
  const click = (e: MouseEvent) => {
    const rect = canvas.getBoundingClientRect(), x = (e.clientX - rect.left) * canvas.width / rect.width, y = (e.clientY - rect.top) * canvas.height / rect.height;
    const id = office.getCharacterAt((x - offset.offsetX) / zoom, (y - offset.offsetY) / zoom);
    const agent = current.find(a => ids.get(a.id) === id);
    if (agent) { office.selectedAgentId = id; selectTask(agent.taskId); }
  };
  canvas.addEventListener('click', click);
  return {
    sync(agents: OfficeAgent[], online: boolean) {
      connected = online; current = agents;
      const present = new Set(agents.map(a => a.id));
      for (const [key, id] of ids) if (!present.has(key)) { office.removeAgent(id); ids.delete(key); }
      for (const agent of agents) {
        let id = ids.get(agent.id);
        const fresh = id === undefined;
        if (fresh) {
          id = sequence++; ids.set(agent.id, id);
          office.addAgent(id, (agent.role === 'Critic' ? 4 : agent.role === 'ExecutorReply' ? 2 : agent.slot) % 6, 0, `seat-${agent.slot % 20}`, true);
          // Existing runs are already at work when a floor opens. New runs walk
          // from distinct aisle tiles, so a simultaneous launch doesn't stack sprites.
          if (hydrated) {
            const ch = office.characters.get(id)!;
            ch.tileCol = 2 + agent.slot % 20; ch.tileRow = 22;
            ch.x = ch.tileCol * TILE_SIZE + 8; ch.y = 22 * TILE_SIZE + 8; ch.state = CharacterState.IDLE;
          }
        }
        const status = agentStatus(agent), ch = office.characters.get(id!)!;
        if (fresh || ch.isActive !== status.active) {
          office.setAgentActive(id!, status.active);
          if (status.active) { office.setAgentTool(id!, agent.role === 'Critic' ? 'Review' : null); office.sendToSeat(id!); }
          else office.walkToTile(id!, 15 + agent.slot % 5, 23);
        }
        if (!status.active && (agent.needsAttention || agent.state !== 'Succeeded')) office.showPermissionBubble(id!);
        else office.clearPermissionBubble(id!);
      }
      canvas.dataset.agents = String(agents.length);
      canvas.dataset.active = String(agents.filter(a => agentStatus(a).active).length);
      hydrated = true;
    },
    dispose() { stop(); resize.disconnect(); canvas.removeEventListener('click', click); }
  };
}
