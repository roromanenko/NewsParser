import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/axios'

export interface ProjectListItemDto {
  id: string
  name: string
  slug: string
  isActive: boolean
}

export function useProjects() {
  return useQuery({
    queryKey: ['projects'],
    queryFn: () =>
      apiClient
        .get<ProjectListItemDto[]>('/projects')
        .then(r => r.data),
  })
}
