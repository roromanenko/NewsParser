import { useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useAuthStore } from '@/store/authStore'
import { useAuth } from './useAuth'
import { Input } from '@/components/ui/Input'
import { Button } from '@/components/ui/Button'

const schema = z.object({
  email: z.string().min(1, 'Email is required').email('Invalid email address'),
  password: z.string().min(6, 'Password must be at least 6 characters'),
})

type FormData = z.infer<typeof schema>

export function LoginPage() {
  const isAuthenticated = useAuthStore(state => state.isAuthenticated)
  const navigate = useNavigate()
  const { login, error, isLoading } = useAuth()

  useEffect(() => {
    if (isAuthenticated()) navigate('/articles', { replace: true })
  }, [isAuthenticated, navigate])

  const { register, handleSubmit, formState: { errors } } = useForm<FormData>({
    resolver: zodResolver(schema),
  })

  const onSubmit = (data: FormData) => login(data.email, data.password)

  return (
    <div className="bg-white rounded-xl shadow-sm border border-gray-200 w-full max-w-md px-8 py-10">
      <div className="mb-8 text-center">
        <h1 className="text-2xl font-bold text-gray-900">NewsParser CMS</h1>
        <p className="text-sm text-gray-500 mt-1">Sign in to your account</p>
      </div>
      <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
        <Input
          label="Email"
          type="email"
          placeholder="you@example.com"
          error={errors.email?.message}
          {...register('email')}
        />
        <Input
          label="Password"
          type="password"
          placeholder="••••••••"
          error={errors.password?.message}
          {...register('password')}
        />
        {error && (
          <div className="rounded-md bg-red-50 border border-red-200 px-4 py-3">
            <p className="text-sm text-red-700">{error}</p>
          </div>
        )}
        <Button type="submit" className="w-full" isLoading={isLoading}>
          Sign in
        </Button>
      </form>
    </div>
  )
}
