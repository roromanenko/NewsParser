import { useQuery } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import type { PublicationListItemDto } from './types'

interface PagedResult<T> {
  items: T[]
  totalCount: number
  totalPages: number
  hasNextPage: boolean
  hasPreviousPage: boolean
  page: number
  pageSize: number
}

export function useAllPublications(page: number, pageSize: number) {
  const { selectedProjectId } = useProjectStore()

  const { data, isLoading, error } = useQuery({
    queryKey: ['project', selectedProjectId, 'publications', 'all', page, pageSize],
    enabled: !!selectedProjectId,
    queryFn: () =>
      apiClient
        .get<PagedResult<PublicationListItemDto>>(`/projects/${selectedProjectId}/publications`, {
          params: { page, pageSize },
        })
        .then(r => r.data),
  })

  return { data, isLoading, error }
}
