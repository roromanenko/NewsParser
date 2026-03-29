import { useQuery } from '@tanstack/react-query'
import { EventsApi } from '@/api/generated'
import { apiClient } from '@/lib/axios'

const eventsApi = new EventsApi(undefined, '', apiClient)

export function useEvents(page: number, pageSize = 20) {
  return useQuery({
    queryKey: ['events', page, pageSize],
    queryFn: async () => {
      const res = await eventsApi.eventsGet(page, pageSize)
      return res.data
    },
  })
}
