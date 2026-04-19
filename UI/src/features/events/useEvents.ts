import { useQuery } from '@tanstack/react-query'
import { EventsApi } from '@/api/generated'
import { apiClient } from '@/lib/axios'

const eventsApi = new EventsApi(undefined, '', apiClient)

export function useEvents(page: number, pageSize = 20, search = '', sortBy = 'newest', tier?: string) {
  return useQuery({
    queryKey: ['events', page, pageSize, search, sortBy, tier],
    queryFn: async () => {
      const res = await eventsApi.eventsGet(page, pageSize, search || undefined, sortBy, tier || undefined)
      return res.data
    },
  })
}
