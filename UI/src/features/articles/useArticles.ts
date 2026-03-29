import { useQuery } from '@tanstack/react-query'
import { ArticlesApi } from '@/api/generated'
import { apiClient } from '@/lib/axios'

const articlesApi = new ArticlesApi(undefined, '', apiClient)

export function useArticles(page: number, pageSize = 20) {
  return useQuery({
    queryKey: ['articles', page, pageSize],
    queryFn: async () => {
      const res = await articlesApi.articlesGet(page, pageSize)
      return res.data
    },
  })
}
