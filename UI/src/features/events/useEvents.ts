import { useQuery } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'

export function useEvents(page: number, pageSize = 20, search = '', sortBy = 'newest', tier?: string) {
  const { selectedProjectId } = useProjectStore()

  return useQuery({
    queryKey: ['project', selectedProjectId, 'events', page, pageSize, search, sortBy, tier],
    enabled: !!selectedProjectId,
    queryFn: async () => {
      const res = await apiClient.get(
        `/projects/${selectedProjectId}/events`,
        { params: { page, pageSize, search: search || undefined, sortBy, tier: tier || undefined } }
      )
      return res.data
    },
  })
}
