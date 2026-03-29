import { useCurrentUser } from './useCurrentUser'

export function usePermissions() {
  const user = useCurrentUser()
  return {
    isAdmin: user?.role === 'Admin',
    isEditor: user?.role === 'Editor',
    canAccessAdmin: user?.role === 'Admin',
  }
}
