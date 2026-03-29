import { useMutation, useQuery } from '@tanstack/react-query'
import { UsersApi, type UserDto } from '@/api/generated'
import { apiClient } from '@/lib/axios'
import { useToast } from '@/context/ToastContext'

const usersApi = new UsersApi(undefined, '', apiClient)

interface UseUsersCallbacks {
  onCreated?: (user: UserDto) => void
  onUpdated?: () => void
  onDeleted?: () => void
}

export function useUsers({ onCreated, onUpdated, onDeleted }: UseUsersCallbacks = {}) {
  const { toast } = useToast()

  const usersQuery = useQuery({
    queryKey: ['editors'],
    queryFn: async () => {
      const res = await usersApi.usersAllGet()
      return res.data
    },
  })

  const createEditor = useMutation({
    mutationFn: (data: { email: string; firstName: string; lastName: string; password: string; role: 'Editor' | 'Admin' }) =>
      usersApi.usersUsersPost(data),
    onSuccess: (res) => {
      toast('User created successfully', 'success')
      onCreated?.(res.data)
      usersQuery.refetch()
    },
    onError: () => toast('Failed to create user', 'error'),
  })

  const updateEditor = useMutation({
    mutationFn: ({ id, ...data }: { id: string; firstName: string; lastName: string; email: string }) =>
      usersApi.usersEditorsIdPut(id, data),
    onSuccess: () => {
      toast('User updated successfully', 'success')
      onUpdated?.()
      usersQuery.refetch()
    },
    onError: () => toast('Failed to update user', 'error'),
  })

  const deleteEditor = useMutation({
    mutationFn: (id: string) => usersApi.usersEditorsIdDelete(id),
    onSuccess: () => {
      toast('User deleted successfully', 'success')
      onDeleted?.()
      usersQuery.refetch()
    },
    onError: () => toast('Failed to delete user', 'error'),
  })

  return { createEditor, updateEditor, deleteEditor, usersQuery }
}
