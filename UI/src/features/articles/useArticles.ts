import { useQuery } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import { ArticlesApi } from '@/api/generated'

const articlesApi = new ArticlesApi(undefined, '', apiClient)

export function useArticles(page: number, pageSize = 20, search = '', sortBy = 'newest') {
  const { selectedProjectId } = useProjectStore()

  return useQuery({
    queryKey: ['project', selectedProjectId, 'articles', page, pageSize, search, sortBy],
    enabled: !!selectedProjectId,
    queryFn: async () => {
      const res = await articlesApi.projectsProjectIdArticlesGet(
        selectedProjectId!,
        page,
        pageSize,
        search || undefined,
        sortBy,
      )
      return res.data
    },
  })
}
