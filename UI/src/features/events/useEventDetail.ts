import { useQuery } from '@tanstack/react-query'
import { EventsApi } from '@/api/generated'
import { apiClient } from '@/lib/axios'

const eventsApi = new EventsApi(undefined, '', apiClient)

export function useEventDetail(id: string) {
  return useQuery({
    queryKey: ['event', id],
    queryFn: async () => {
      const res = await eventsApi.eventsIdGet(id)
      return res.data
    },
    enabled: !!id,
  })
}
