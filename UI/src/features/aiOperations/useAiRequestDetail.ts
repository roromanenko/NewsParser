import { useQuery } from '@tanstack/react-query'
import type { UseQueryResult } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import type { AiOpsRequestRow } from './types'
import { mapRequestRow } from './mappers'

export function useAiRequestDetail(id: string | null): UseQueryResult<AiOpsRequestRow> {
  const { selectedProjectId } = useProjectStore()

  return useQuery({
    queryKey: ['project', selectedProjectId, 'ai-ops', 'request', id],
    enabled: !!id && !!selectedProjectId,
    queryFn: async () => {
      const res = await apiClient.get(`/projects/${selectedProjectId}/ai-operations/requests/${id}`)
      return mapRequestRow(res.data)
    },
  })
}
