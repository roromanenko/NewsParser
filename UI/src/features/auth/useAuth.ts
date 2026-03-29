import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { AuthApi } from '@/api/generated'
import { apiClient } from '@/lib/axios'
import { useAuthStore } from '@/store/authStore'

const authApi = new AuthApi(undefined, '', apiClient)

export function useAuth() {
  const setUser = useAuthStore(state => state.setUser)
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(false)

  async function login(email: string, password: string) {
    setIsLoading(true)
    setError(null)
    try {
      const res = await authApi.authLoginPost({ email, password })
      const data = res.data
      if (!data.userId || !data.email || !data.role || !data.token) {
        throw new Error('Invalid response')
      }
      setUser({
        userId: data.userId,
        email: data.email,
        role: data.role as 'Admin' | 'Editor',
        token: data.token,
      })
      navigate('/articles')
    } catch {
      setError('Invalid email or password. Please try again.')
    } finally {
      setIsLoading(false)
    }
  }

  return { login, error, isLoading }
}
