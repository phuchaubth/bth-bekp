import { Box, Typography } from '@mui/material';
import type { OperationalEvent } from '../api/types';
import { formatTime } from '../lib/format';
import { describeEvent } from '../lib/labels';
import { eventToneColor } from '../lib/ui';
import { Panel } from './Panel';

export function EventFeed({ events, title = 'Sự kiện trực tiếp' }: { events: OperationalEvent[]; title?: string }) {
  return (
    <Panel title={title}>
      {events.length === 0 ? (
        <Typography sx={{ fontSize: 14, color: 'text.secondary' }}>Chưa có sự kiện.</Typography>
      ) : (
        <Box component="ol" aria-live="polite" sx={{ listStyle: 'none', m: 0, p: 0, display: 'flex', flexDirection: 'column', gap: 0.5 }}>
          {events.map((event) => {
            const { label, text, tone } = describeEvent(event);
            return (
              <Box component="li" key={event.id} sx={{ display: 'flex', gap: 1.5, alignItems: 'center', p: 1.25, borderRadius: 2.5 }}>
                <Box aria-hidden="true" sx={{ flex: 'none', width: 10, height: 10, borderRadius: '3px', bgcolor: eventToneColor[tone] }} />
                <Box sx={{ flex: 1, minWidth: 0 }}>
                  <Typography sx={{ fontSize: 13 }}>
                    <strong>{label}</strong> · {text}
                  </Typography>
                  {event.departmentName ? <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>{event.departmentName}</Typography> : null}
                </Box>
                <Typography component="time" dateTime={event.occurredAtUtc} sx={{ flex: 'none', fontSize: 12, color: 'text.secondary' }}>
                  {formatTime(event.occurredAtUtc)}
                </Typography>
              </Box>
            );
          })}
        </Box>
      )}
    </Panel>
  );
}
