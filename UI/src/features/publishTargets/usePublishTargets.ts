import { useQuery } from '@tanstack/react-query'
import { PublishTargetsApi } from '@/api/generated'
import { apiClient } from '@/lib/axios'

const publishTargetsApi = new PublishTargetsApi(undefined, '', apiClient)

export function usePublishTargets() {
  return useQuery({
    queryKey: ['publishTargets'],
    queryFn: () => publishTargetsApi.publishTargetsGet().then(r => r.data),
  })
}

export function useActivePublishTargets() {
  return useQuery({
    queryKey: ['publishTargets', 'active'],
    queryFn: () => publishTargetsApi.publishTargetsActiveGet().then(r => r.data),
  })
}
