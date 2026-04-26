import { useQuery } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'

export function useArticles(page: number, pageSize = 20, search = '', sortBy = 'newest') {
  const { selectedProjectId } = useProjectStore()

  return useQuery({
    queryKey: ['project', selectedProjectId, 'articles', page, pageSize, search, sortBy],
    enabled: !!selectedProjectId,
    queryFn: async () => {
      const res = await apiClient.get(
        `/projects/${selectedProjectId}/articles`,
        { params: { page, pageSize, search: search || undefined, sortBy } }
      )
      return res.data
    },
  })
}
