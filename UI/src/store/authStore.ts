import { create } from 'zustand'
import { persist } from 'zustand/middleware'

interface AuthUser {
  userId: string
  email: string
  role: 'Admin' | 'Editor'
  token: string
}

interface AuthStore {
  user: AuthUser | null
  setUser: (user: AuthUser) => void
  logout: () => void
  isAuthenticated: () => boolean
}

export const useAuthStore = create<AuthStore>()(
  persist(
    (set, get) => ({
      user: null,
      setUser: (user) => set({ user }),
      logout: () => set({ user: null }),
      isAuthenticated: () => get().user !== null,
    }),
    {
      name: 'auth-storage',
    }
  )
)