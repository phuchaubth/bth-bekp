import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { getAccessToken } from '../api/client';
import { appUrl } from '../lib/basePath';
import type { OperationalEvent } from '../api/types';
import { RealtimeContext, type ConnectionStatus } from './context';

const MAX_LIVE_EVENTS = 40;
const INVALIDATE_DEBOUNCE_MS = 400;

/** Single SignalR connection per tab. Each pushed event refreshes the affected dashboard queries. */
export function RealtimeProvider({ children, hubUrl = appUrl('/hubs/dashboard') }: { children: ReactNode; hubUrl?: string }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<ConnectionStatus>('connecting');
  const [events, setEvents] = useState<OperationalEvent[]>([]);
  const [lastEventAt, setLastEventAt] = useState<Date | null>(null);
  const pending = useRef(new Set<string>());
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl, { accessTokenFactory: async () => (await getAccessToken()) ?? '' })
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      .configureLogging(LogLevel.Warning)
      .build();

    const flush = () => {
      const departments = [...pending.current];
      pending.current.clear();
      void queryClient.invalidateQueries({ queryKey: ['overview'] });
      void queryClient.invalidateQueries({ queryKey: ['timeStandards'] });
      for (const code of departments) {
        void queryClient.invalidateQueries({ queryKey: ['department', code] });
      }
    };

    connection.on('operationalEvent', (event: OperationalEvent) => {
      setEvents((previous) => [event, ...previous.filter((e) => e.id !== event.id)].slice(0, MAX_LIVE_EVENTS));
      setLastEventAt(new Date());
      if (event.departmentCode) {
        pending.current.add(event.departmentCode);
      }

      clearTimeout(timer.current);
      timer.current = setTimeout(flush, INVALIDATE_DEBOUNCE_MS);
    });
    connection.onreconnecting(() => setStatus('reconnecting'));
    connection.onreconnected(() => {
      setStatus('connected');
      void queryClient.invalidateQueries();
    });
    connection.onclose(() => setStatus('disconnected'));

    let cancelled = false;
    let retry: ReturnType<typeof setTimeout> | undefined;
    const start = async () => {
      try {
        await connection.start();
        if (!cancelled) {
          setStatus('connected');
        }
      } catch {
        if (!cancelled) {
          setStatus('disconnected');
          retry = setTimeout(() => void start(), 10_000);
        }
      }
    };
    void start();

    return () => {
      cancelled = true;
      clearTimeout(retry);
      clearTimeout(timer.current);
      if (connection.state !== HubConnectionState.Disconnected) {
        void connection.stop();
      }
    };
  }, [hubUrl, queryClient]);

  const value = useMemo(() => ({ status, events, lastEventAt }), [status, events, lastEventAt]);
  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>;
}
