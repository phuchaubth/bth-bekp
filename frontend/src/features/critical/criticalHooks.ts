import { useQueryClient } from '@tanstack/react-query'
import { criticalKeys, type CriticalResult } from '../../api/critical'

/** Cập nhật hồ sơ vừa đổi vào bộ nhớ đệm và làm mới các danh sách, cảnh báo, sổ, dashboard. */
export function useCriticalSaved() {
  const queryClient = useQueryClient()
  return (result: CriticalResult) => {
    queryClient.setQueryData(criticalKeys.detail(result.id), result)
    void queryClient.invalidateQueries({ queryKey: criticalKeys.records })
  }
}
