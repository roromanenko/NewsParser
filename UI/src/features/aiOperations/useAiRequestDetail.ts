import { useQuery } from '@tanstack/react-query'
import type { UseQueryResult } from '@tanstack/react-query'
import { apiClient } from '@/lib/axios'
import type { AiOpsRequestRow } from './types'
import { mapRequestRow } from './mappers'

export function useAiRequestDetail(id: string | null): UseQueryResult<AiOpsRequestRow> {
  return useQuery({
    queryKey: ['ai-ops', 'request', id],
    enabled: !!id,
    queryFn: async () => {
      const res = await apiClient.get(`/ai-operations/requests/${id}`)
      return mapRequestRow(res.data)
    },
  })
}
