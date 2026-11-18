import { createTheme } from '@mui/material/styles';
import { tokens } from './lib/levels';

const fontFamily = "'Be Vietnam Pro', system-ui, -apple-system, 'Segoe UI', sans-serif";

export const lightTheme = createTheme({
  palette: {
    mode: 'light',
    primary: { main: tokens.navy },
    text: { primary: tokens.ink, secondary: tokens.muted },
    background: { default: tokens.page, paper: tokens.card },
    divider: tokens.border,
  },
  shape: { borderRadius: 12 },
  typography: {
    fontFamily,
    button: { textTransform: 'none', fontWeight: 600 },
  },
  components: {
    MuiPaper: { defaultProps: { elevation: 0 } },
    MuiCssBaseline: {
      styleOverrides: {
        body: { fontVariantNumeric: 'tabular-nums' },
        '@media (prefers-reduced-motion: reduce)': { '*': { animation: 'none !important', transition: 'none !important' } },
      },
    },
  },
});

export const wallboardTheme = createTheme({
  palette: {
    mode: 'dark',
    primary: { main: '#5B9BE6' },
    text: { primary: '#E6EDF7', secondary: '#9AA8BC' },
    background: { default: '#070D18', paper: '#0F1A2B' },
    divider: '#1E2B40',
  },
  typography: { fontFamily },
  components: {
    MuiPaper: { defaultProps: { elevation: 0 } },
    MuiCssBaseline: { styleOverrides: { body: { fontVariantNumeric: 'tabular-nums' } } },
  },
});
