import { CssBaseline, ThemeProvider } from '@mui/material';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { routerBasename } from './lib/basePath';
import { DepartmentIndexRedirect, DepartmentPage } from './pages/DepartmentPage';
import { OverviewPage } from './pages/OverviewPage';
import { WallboardPage } from './pages/WallboardPage';
import { RealtimeProvider } from './realtime/RealtimeProvider';
import { lightTheme } from './theme';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 15_000, retry: 2, refetchOnWindowFocus: true },
  },
});

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <RealtimeProvider>
        <ThemeProvider theme={lightTheme}>
          <CssBaseline />
          <BrowserRouter basename={routerBasename() || undefined}>
            <Routes>
              <Route path="/" element={<OverviewPage />} />
              <Route path="/departments" element={<DepartmentIndexRedirect />} />
              <Route path="/departments/:code" element={<DepartmentPage />} />
              <Route path="/wallboard" element={<WallboardPage />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </BrowserRouter>
        </ThemeProvider>
      </RealtimeProvider>
    </QueryClientProvider>
  );
}
