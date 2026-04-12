import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/axios'
import type { PublicationListItemDto } from './types'

export function usePublications(eventId: string) {
  const { data, isLoading, error } = useQuery({
    queryKey: ['publications', 'by-event', eventId],
    queryFn: () =>
      apiClient
        .get<PublicationListItemDto[]>(`/publications/by-event/${eventId}`)
        .then(r => r.data),
    enabled: !!eventId,
  })

  return { publications: data ?? [], isLoading, error }
}
