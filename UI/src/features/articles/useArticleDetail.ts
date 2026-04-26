import { useQuery } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'

export function useArticleDetail(id: string) {
  const { selectedProjectId } = useProjectStore()

  return useQuery({
    queryKey: ['project', selectedProjectId, 'article', id],
    enabled: !!id && !!selectedProjectId,
    queryFn: async () => {
      const res = await apiClient.get(`/projects/${selectedProjectId}/articles/${id}`)
      return res.data
    },
  })
}
