import { useQuery } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import { EventsApi } from '@/api/generated'

const eventsApi = new EventsApi(undefined, '', apiClient)

export function useEventDetail(id: string) {
  const { selectedProjectId } = useProjectStore()

  return useQuery({
    queryKey: ['project', selectedProjectId, 'event', id],
    enabled: !!id && !!selectedProjectId,
    queryFn: async () => {
      const res = await eventsApi.projectsProjectIdEventsIdGet(id, selectedProjectId!)
      return res.data
    },
  })
}
