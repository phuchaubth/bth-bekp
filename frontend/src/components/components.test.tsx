import { ThemeProvider } from '@mui/material';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, within } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { OverviewPage } from '../pages/OverviewPage';
import { overview, rooms, timeStandards } from '../test/fixtures';
import { lightTheme } from '../theme';
import { AlertsPanel } from './AlertsPanel';
import { BedMap } from './BedMap';
import { BloodInventoryPanel } from './BloodInventoryPanel';
import { DepartmentTable } from './DepartmentTable';
import { DiseasePatternPanel } from './DiseasePatternPanel';

function wrap(ui: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <ThemeProvider theme={lightTheme}>
        <MemoryRouter>{ui}</MemoryRouter>
      </ThemeProvider>
    </QueryClientProvider>,
  );
}

afterEach(() => vi.restoreAllMocks());

describe('dashboard components', () => {
  it('lists departments with occupancy and links to the department screen', () => {
    wrap(<DepartmentTable departments={overview().departments} />);
    const rows = screen.getAllByRole('row');
    expect(within(rows[1]!).getByText('Bạch cầu cấp người lớn')).toBeInTheDocument();
    expect(within(rows[1]!).getByText('97,9%')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Xem chi tiết khoa Bạch cầu cấp người lớn' })).toHaveAttribute('href', '/departments/DEP-0001');
  });

  it('renders alerts with severity for screen readers', () => {
    wrap(<AlertsPanel alerts={overview().alerts} />);
    expect(screen.getByText('2 mở')).toBeInTheDocument();
    expect(screen.getByText('Nghiêm trọng:', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('Tiểu cầu nhóm O dưới mức tối thiểu')).toBeInTheDocument();
  });

  it('labels blood stock cells accessibly', () => {
    wrap(<BloodInventoryPanel inventory={overview().bloodInventory} />);
    expect(screen.getByLabelText('Tiểu cầu nhóm O: 9 đơn vị, tối thiểu 20, thiếu')).toHaveTextContent('9');
  });

  it('shows the disease pattern by ICD-10 group', () => {
    wrap(<DiseasePatternPanel pattern={overview().diseasePattern} />);
    expect(screen.getByRole('rowheader', { name: 'Lơ-xê-mi cấp dòng tủy' })).toBeInTheDocument();
    expect(screen.getByText('(37,5%)')).toBeInTheDocument();
  });

  it('filters and selects beds on the bed map', () => {
    const onFilter = vi.fn();
    const onSelect = vi.fn();
    wrap(<BedMap rooms={rooms()} sterile={false} filter="neutropenic" onFilterChange={onFilter} selected={null} onSelect={onSelect} />);

    expect(screen.getByRole('button', { name: /Giường 201-1, Nặng · theo dõi sát, đang truyền máu/ })).toHaveStyle({ opacity: '1' });
    expect(screen.getByRole('button', { name: /Giường 201-2/ })).toHaveStyle({ opacity: '0.3' });

    fireEvent.click(screen.getByRole('button', { name: 'Trống / vệ sinh 2' }));
    expect(onFilter).toHaveBeenCalledWith('free');
    fireEvent.click(screen.getByRole('button', { name: /Giường 201-3/ }));
    expect(onSelect).toHaveBeenCalledWith('201-3');
  });
});

describe('OverviewPage', () => {
  it('loads the overview and filters KPI totals by department group', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      const body = url.includes('time-standards') ? timeStandards() : overview();
      return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });

    wrap(<OverviewPage />);

    expect(await screen.findByText('Tổng quan khoa lâm sàng')).toBeInTheDocument();
    const kpis = await screen.findByRole('region', { name: 'Chỉ số chính' });
    expect(within(kpis).getByText('90,9%')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Huyết học lành tính' }));
    expect(within(kpis).getByText('82,5%')).toBeInTheDocument();
    expect(await screen.findByText('Phát máu – yêu cầu cấp cứu')).toBeInTheDocument();
    expect(screen.getByText('Chưa xác nhận · Mục tiêu tạm – cần xác nhận')).toBeInTheDocument();
  });
});
