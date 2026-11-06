import assert from 'node:assert/strict';
import test from 'node:test';
import { agentStatus, allocateSeats, SEATS_PER_FLOOR } from '../src/office-model.js';

test('100 active agents get unique seats on five floors; recent agents also fit', () => {
  const agents = Array.from({ length: 120 }, (_, i) => ({ id: String(i) }));
  const seats = allocateSeats(new Map(), agents);
  assert.equal(new Set(seats.values()).size, 120);
  assert.equal(Math.floor(seats.get('99') / SEATS_PER_FLOOR), 4);
  assert.equal(Math.floor(seats.get('119') / SEATS_PER_FLOOR), 5);
});
test('snapshots keep seats stable when order changes or a run finishes', () => {
  const first = allocateSeats(new Map(), [{ id: 'a' }, { id: 'b' }, { id: 'c' }]);
  const next = allocateSeats(first, [{ id: 'c' }, { id: 'b', state: 'Succeeded' }, { id: 'd' }]);
  assert.equal(next.get('b'), first.get('b'));
  assert.equal(next.get('c'), first.get('c'));
  assert.equal(next.get('d'), first.get('a'));
  assert.equal(next.has('a'), false);
});
test('only running processes animate work; review, error and completion are distinct', () => {
  assert.equal(agentStatus({ state: 'Running', runner: 'Mock' }).active, true);
  assert.equal(agentStatus({ state: 'Succeeded', waitingForReview: true }).label, 'Ждёт ревью человека');
  for (const state of ['Failed', 'Interrupted', 'Cancelled']) {
    const status = agentStatus({ state }); assert.equal(status.active, false); assert.equal(status.tone, 'red');
  }
  assert.equal(agentStatus({ state: 'Succeeded' }).active, false);
  assert.equal(agentStatus({ state: 'Succeeded', needsAttention: true }).tone, 'red');
});
