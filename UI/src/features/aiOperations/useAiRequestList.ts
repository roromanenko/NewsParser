import { useQuery, keepPreviousData } from '@tanstack/react-query'
import type { UseQueryResult } from '@tanstack/react-query'
import { apiClient } from '@/lib/axios'
import { AiOperationsApi } from '@/api/generated'
import type { AiOpsFilters, AiOpsRequestPage } from './types'
import { mapRequestRow } from './mappers'

const aiOpsApi = new AiOperationsApi(undefined, '', apiClient)

const LIST_STALE_MS = 10_000
export const DEFAULT_PAGE_SIZE = 20

export function useAiRequestList(
  page: number,
  pageSize: number,
  filters: AiOpsFilters,
): UseQueryResult<AiOpsRequestPage> {
  return useQuery({
    queryKey: ['ai-ops', 'requests', page, pageSize, filters],
    staleTime: LIST_STALE_MS,
    placeholderData: keepPreviousData,
    queryFn: async () => {
      const res = await aiOpsApi.aiOperationsRequestsGet(
        filters.from || undefined,
        filters.to || undefined,
        filters.provider || undefined,
        filters.worker || undefined,
        filters.model || undefined,
        filters.status || undefined,
        filters.search || undefined,
        page,
        pageSize,
      )
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
