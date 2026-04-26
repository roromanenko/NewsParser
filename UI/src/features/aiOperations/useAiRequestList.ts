import { useQuery, keepPreviousData } from '@tanstack/react-query'
import type { UseQueryResult } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import type { AiOpsFilters, AiOpsRequestPage } from './types'
import { mapRequestRow } from './mappers'

const LIST_STALE_MS = 10_000
export const DEFAULT_PAGE_SIZE = 20

export function useAiRequestList(
  page: number,
  pageSize: number,
  filters: AiOpsFilters,
): UseQueryResult<AiOpsRequestPage> {
  const { selectedProjectId } = useProjectStore()

  return useQuery({
    queryKey: ['project', selectedProjectId, 'ai-ops', 'requests', page, pageSize, filters],
    enabled: !!selectedProjectId,
    staleTime: LIST_STALE_MS,
    placeholderData: keepPreviousData,
    queryFn: async () => {
      const res = await apiClient.get(`/projects/${selectedProjectId}/ai-operations/requests`, {
        params: {
          from: filters.from || undefined,
          to: filters.to || undefined,
          provider: filters.provider || undefined,
          worker: filters.worker || undefined,
          model: filters.model || undefined,
          status: filters.status || undefined,
          search: filters.search || undefined,
          page,
          pageSize,
        },
      })
      const dto = res.data

      return {
        items: (dto.items ?? []).map(mapRequestRow),
        page: dto.page ?? 0,
        pageSize: dto.pageSize ?? 0,
        totalCount: dto.totalCount ?? 0,
        totalPages: dto.totalPages ?? 0,
        hasNextPage: dto.hasNextPage ?? false,
        hasPreviousPage: dto.hasPreviousPage ?? false,
      } satisfies AiOpsRequestPage
    },
  })
}
