import { useQuery } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'

export function useSources() {
  const { selectedProjectId } = useProjectStore()

  return useQuery({
    queryKey: ['project', selectedProjectId, 'sources'],
    enabled: !!selectedProjectId,
    queryFn: async () => {
      const res = await apiClient.get(`/projects/${selectedProjectId}/sources`)
      return res.data
    },
  })
}
