import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { AuthApi } from '@/api/generated'
import { apiClient } from '@/lib/axios'
import { useAuthStore } from '@/store/authStore'

const authApi = new AuthApi(undefined, '', apiClient)

export function useRegister() {
  const setUser = useAuthStore(state => state.setUser)
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(false)

  async function register(email: string, firstName: string, lastName: string, password: string) {
    setIsLoading(true)
    setError(null)
    try {
      const res = await authApi.authRegisterPost({ email, firstName, lastName, password })
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
      setError('Registration failed. The email may already be in use.')
    } finally {
      setIsLoading(false)
    }
  }

  return { register, error, isLoading }
}
