import { useQuery } from '@tanstack/react-query'
import { ApiError, apiBlob, apiRequest } from './http'

/** Chữ ký mẫu của chính mình (hiện cạnh khung ký khi xác nhận phiếu để đối chiếu). */
export const accountApi = {
  /** Ảnh PNG chữ ký mẫu; chưa có thì null. */
  signatureSpecimen: async (signal?: AbortSignal): Promise<Blob | null> => {
    try {
      return await apiBlob('/api/v1/account/signature-specimen', signal)
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) return null
      throw e
    }
  },
  saveSignatureSpecimen: (image: string) =>
    apiRequest<{ updatedAtUtc: string }>('/api/v1/account/signature-specimen', { method: 'PUT', body: { image } }),
  deleteSignatureSpecimen: () => apiRequest<void>('/api/v1/account/signature-specimen', { method: 'DELETE' }),
}

export const accountKeys = { signatureSpecimen: ['account', 'signature-specimen'] as const }

export function useSignatureSpecimen() {
  return useQuery({ queryKey: accountKeys.signatureSpecimen, queryFn: ({ signal }) => accountApi.signatureSpecimen(signal) })
}
