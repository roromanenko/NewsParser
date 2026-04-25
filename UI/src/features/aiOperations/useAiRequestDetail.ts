import { useQuery } from '@tanstack/react-query'
import type { UseQueryResult } from '@tanstack/react-query'
import { AiOperationsApi } from '@/api/generated'
import { apiClient } from '@/lib/axios'
import type { AiOpsRequestRow } from './types'
import { mapRequestRow } from './mappers'

const aiOpsApi = new AiOperationsApi(undefined, '', apiClient)

export function useAiRequestDetail(id: string | null): UseQueryResult<AiOpsRequestRow> {
  return useQuery({
    queryKey: ['ai-ops', 'request', id],
    enabled: !!id,
    queryFn: async () => {
      const res = await aiOpsApi.aiOperationsRequestsIdGet(id!)
      return mapRequestRow(res.data)
    },
  })
}
