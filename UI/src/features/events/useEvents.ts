import { useQuery } from '@tanstack/react-query'
import { EventsApi } from '@/api/generated'
import { apiClient } from '@/lib/axios'

const eventsApi = new EventsApi(undefined, '', apiClient)

export function useEvents(page: number, pageSize = 20, search = '', sortBy = 'newest') {
  return useQuery({
    queryKey: ['events', page, pageSize, search, sortBy],
    queryFn: async () => {
      const res = await eventsApi.eventsGet(page, pageSize, search || undefined, sortBy)
      return res.data
    },
  })
}
