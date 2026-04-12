import { useQuery } from '@tanstack/react-query'
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
  const { data, isLoading, error } = useQuery({
    queryKey: ['publications', 'all', page, pageSize],
    queryFn: () =>
      apiClient
        .get<PagedResult<PublicationListItemDto>>('/publications', { params: { page, pageSize } })
        .then(r => r.data),
  })

  return { data, isLoading, error }
}
