import { useQuery } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import { PublicationsApi } from '@/api/generated'

const publicationsApi = new PublicationsApi(undefined, '', apiClient)

export function usePublications(eventId: string) {
  const { selectedProjectId } = useProjectStore()

  const { data, isLoading, error } = useQuery({
    queryKey: ['project', selectedProjectId, 'publications', 'by-event', eventId],
    enabled: !!eventId && !!selectedProjectId,
    queryFn: () =>
      publicationsApi
        .projectsProjectIdPublicationsByEventEventIdGet(eventId, selectedProjectId!)
        .then(r => r.data),
  })

  return { publications: data ?? [], isLoading, error }
}
