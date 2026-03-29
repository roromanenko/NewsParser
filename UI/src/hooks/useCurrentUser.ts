import { useAuthStore } from '@/store/authStore'

export function useCurrentUser() {
  return useAuthStore(state => state.user)
}
