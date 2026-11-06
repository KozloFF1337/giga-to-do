export const SEATS_PER_FLOOR = 20;
export const roles = { Executor: 'Исполнитель', Critic: 'Критик', ExecutorReply: 'Ответ критику' };
export function agentStatus(agent) {
  if (agent.state === 'Running') return { label: 'Работает', tone: 'green', active: true };
  if (agent.state !== 'Succeeded' || agent.needsAttention) return { label: 'Требуется внимание', tone: 'red', active: false };
  if (agent.waitingForReview) return { label: 'Ждёт ревью человека', tone: 'amber', active: false };
  return { label: 'Запуск завершён', tone: '', active: false };
}
// Keep seats stable across snapshots, including out-of-order server lists.
// Completed runs linger only for the server's 45-second recent window.
export function allocateSeats(previous, agents) {
  const ids = new Set(agents.map(a => a.id));
  const result = new Map([...previous].filter(([id]) => ids.has(id)));
  const used = new Set(result.values());
  for (const agent of agents) {
    if (result.has(agent.id)) continue;
    let slot = 0; while (used.has(slot)) slot++;
    result.set(agent.id, slot); used.add(slot);
  }
  return result;
}
