import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/axios'
import { ProjectsApi } from '@/api/generated'
export type { ProjectListItemDto } from '@/api/generated'

const projectsApi = new ProjectsApi(undefined, '', apiClient)

export function useProjects() {
  return useQuery({
    queryKey: ['projects'],
    queryFn: () =>
      projectsApi.projectsGet().then(r => r.data),
  })
}
