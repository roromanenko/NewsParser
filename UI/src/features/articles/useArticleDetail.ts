import { useQuery } from '@tanstack/react-query'
import { ArticlesApi } from '@/api/generated'
import { apiClient } from '@/lib/axios'

const articlesApi = new ArticlesApi(undefined, '', apiClient)

export function useArticleDetail(id: string) {
  return useQuery({
    queryKey: ['article', id],
    queryFn: async () => {
      const res = await articlesApi.articlesIdGet(id)
      return res.data
    },
    enabled: !!id,
  })
}
