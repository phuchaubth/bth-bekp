import { Alert, Box, CircularProgress } from '@mui/material';

export function Loading() {
  return (
    <Box role="status" aria-label="Đang tải" sx={{ display: 'flex', justifyContent: 'center', py: 10 }}>
      <CircularProgress />
    </Box>
  );
}

export function LoadError({ message = 'Không tải được dữ liệu. Hệ thống sẽ thử lại.' }: { message?: string }) {
  return (
    <Alert severity="error" sx={{ my: 3 }}>
      {message}
    </Alert>
  );
}

export function SyntheticDataBadge() {
  return (
    <Box component="span" sx={{ display: 'inline-block', px: 1, py: 0.25, borderRadius: 1.5, bgcolor: '#E9ECF1', color: '#3D4756', fontSize: 12, fontWeight: 600 }}>
      Dữ liệu mô phỏng
    </Box>
  );
}
