import { useQuery } from '@tanstack/react-query'
import { SourcesApi } from '@/api/generated'
import { apiClient } from '@/lib/axios'

const sourcesApi = new SourcesApi(undefined, '', apiClient)

export function useSources() {
  return useQuery({
    queryKey: ['sources'],
    queryFn: async () => {
      const res = await sourcesApi.sourcesGet()
      return res.data
    },
  })
}
