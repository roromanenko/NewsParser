import { useQuery } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import type { PublicationListItemDto } from './types'

export function usePublications(eventId: string) {
  const { selectedProjectId } = useProjectStore()

  const { data, isLoading, error } = useQuery({
    queryKey: ['project', selectedProjectId, 'publications', 'by-event', eventId],
    enabled: !!eventId && !!selectedProjectId,
    queryFn: () =>
      apiClient
        .get<PublicationListItemDto[]>(`/projects/${selectedProjectId}/publications/by-event/${eventId}`)
        .then(r => r.data),
  })

  return { publications: data ?? [], isLoading, error }
}
