import { useQuery } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'

export function useEventDetail(id: string) {
  const { selectedProjectId } = useProjectStore()

  return useQuery({
    queryKey: ['project', selectedProjectId, 'event', id],
    enabled: !!id && !!selectedProjectId,
    queryFn: async () => {
      const res = await apiClient.get(`/projects/${selectedProjectId}/events/${id}`)
      return res.data
    },
  })
}
