import { useQuery } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import { EventsApi } from '@/api/generated'

const eventsApi = new EventsApi(undefined, '', apiClient)

export function useEvents(page: number, pageSize = 20, search = '', sortBy = 'newest', tier?: string) {
  const { selectedProjectId } = useProjectStore()

  return useQuery({
    queryKey: ['project', selectedProjectId, 'events', page, pageSize, search, sortBy, tier],
    enabled: !!selectedProjectId,
    queryFn: async () => {
      const res = await eventsApi.projectsProjectIdEventsGet(
        selectedProjectId!,
        page,
        pageSize,
        search || undefined,
        sortBy,
        tier || undefined,
      )
      return res.data
    },
  })
}
