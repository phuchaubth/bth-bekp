import { createContext, useContext } from 'react';
import type { OperationalEvent } from '../api/types';

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

export interface RealtimeState {
  status: ConnectionStatus;
  events: OperationalEvent[];
  lastEventAt: Date | null;
}

export const RealtimeContext = createContext<RealtimeState>({ status: 'connecting', events: [], lastEventAt: null });

export const useRealtime = (): RealtimeState => useContext(RealtimeContext);

/** Merges events loaded with the snapshot and events pushed since, newest first, without duplicates. */
export function mergeEvents(snapshot: OperationalEvent[], live: OperationalEvent[], limit = 12): OperationalEvent[] {
  const seen = new Set<string>();
  return [...live, ...snapshot]
    .filter((e) => (seen.has(e.id) ? false : (seen.add(e.id), true)))
    .sort((a, b) => b.occurredAtUtc.localeCompare(a.occurredAtUtc))
    .slice(0, limit);
}
