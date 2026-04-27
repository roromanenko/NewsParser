import { useQuery } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import { ArticlesApi } from '@/api/generated'

const articlesApi = new ArticlesApi(undefined, '', apiClient)

export function useArticleDetail(id: string) {
  const { selectedProjectId } = useProjectStore()

  return useQuery({
    queryKey: ['project', selectedProjectId, 'article', id],
    enabled: !!id && !!selectedProjectId,
    queryFn: async () => {
      const res = await articlesApi.projectsProjectIdArticlesIdGet(id, selectedProjectId!)
      return res.data
    },
  })
}
