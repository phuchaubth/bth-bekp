import { Box, Paper, Typography } from '@mui/material';
import type { ReactNode } from 'react';

interface PanelProps {
  title: string;
  subtitle?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
  padded?: boolean;
  'aria-label'?: string;
}

/** Outlined card used by every dashboard section. */
export function Panel({ title, subtitle, actions, children, padded = true, ...rest }: PanelProps) {
  return (
    <Paper component="section" aria-label={rest['aria-label'] ?? title} variant="outlined" sx={{ borderRadius: 4, overflow: 'hidden', minWidth: 0 }}>
      <Box sx={{ px: 2.5, pt: 2, pb: padded ? 0 : 2, display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 1.5 }}>
        <Box sx={{ minWidth: 0 }}>
          <Typography component="h2" sx={{ fontSize: 17, fontWeight: 700 }}>
            {title}
          </Typography>
          {subtitle ? (
            <Typography component="div" sx={{ fontSize: 12, color: 'text.secondary', mt: 0.25 }}>
              {subtitle}
            </Typography>
          ) : null}
        </Box>
        {actions}
      </Box>
      <Box sx={{ p: padded ? 2.5 : 0, pt: padded ? 2 : 0 }}>{children}</Box>
    </Paper>
  );
}
