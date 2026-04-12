import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/axios'
import type { PublicationDetailDto } from './types'

export function usePublicationDetail(id: string) {
  const { data, isLoading, error } = useQuery({
    queryKey: ['publication', id],
    queryFn: () =>
      apiClient
        .get<PublicationDetailDto>(`/publications/${id}`)
        .then(r => r.data),
    enabled: !!id,
  })

  return { publication: data, isLoading, error }
}
