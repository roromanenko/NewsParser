import { useEffect } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useAuthStore } from '@/store/authStore'
import { useAuth } from './useAuth'

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
    <div className="w-full max-w-md">
      {/* Masthead */}
      <div className="text-center mb-8">
        <div
          className="font-caps text-xs tracking-[0.4em] mb-3"
          style={{ color: 'var(--caramel)' }}
        >
          EST. 2024
        </div>
        <h1
          className="font-display text-5xl mb-2"
          style={{ color: '#E8E8E8' }}
        >
          Panoptis
        </h1>
        <div className="flex items-center gap-3 justify-center">
          <div className="flex-1 h-px" style={{ background: 'rgba(255,255,255,0.15)' }} />
          <span className="font-caps text-xs tracking-widest" style={{ color: 'var(--rust)' }}>
            CMS
          </span>
          <div className="flex-1 h-px" style={{ background: 'rgba(255,255,255,0.15)' }} />
        </div>
      </div>

      {/* Card */}
      <div
        className="border p-8"
        style={{
          background: 'rgba(61,15,15,0.4)',
          borderColor: 'rgba(255,255,255,0.1)',
        }}
      >
        {/* Card header */}
        <div className="mb-6 pb-4 border-b" style={{ borderColor: 'rgba(255,255,255,0.1)' }}>
          <div className="font-caps text-xs tracking-widest mb-1" style={{ color: 'var(--caramel)' }}>
            SECURE ACCESS
          </div>
          <p className="font-mono text-sm" style={{ color: '#9ca3af' }}>
            Sign in to your account
          </p>
        </div>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
          {/* Email */}
          <div>
            <label className="block font-caps text-xs tracking-widest mb-2" style={{ color: 'var(--caramel)' }}>
              EMAIL
            </label>
            <input
              type="email"
              placeholder="you@example.com"
              className="w-full px-4 py-3 font-mono text-sm border focus:outline-none transition-colors"
              style={{
                background: 'var(--burgundy)',
                borderColor: errors.email ? 'var(--crimson)' : 'rgba(255,255,255,0.1)',
                color: '#E8E8E8',
              }}
              onFocus={e => {
                if (!errors.email) e.currentTarget.style.borderColor = 'var(--caramel)'
              }}
              {...register('email')}
              onBlur={e => {
                if (!errors.email) e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)'
              }}
            />
            {errors.email && (
              <p className="mt-1 font-mono text-xs" style={{ color: 'var(--crimson)' }}>
                {errors.email.message}
              </p>
            )}
          </div>

          {/* Password */}
          <div>
            <label className="block font-caps text-xs tracking-widest mb-2" style={{ color: 'var(--caramel)' }}>
              PASSWORD
            </label>
            <input
              type="password"
              placeholder="••••••••"
              className="w-full px-4 py-3 font-mono text-sm border focus:outline-none transition-colors"
              style={{
                background: 'var(--burgundy)',
                borderColor: errors.password ? 'var(--crimson)' : 'rgba(255,255,255,0.1)',
                color: '#E8E8E8',
              }}
              onFocus={e => {
                if (!errors.password) e.currentTarget.style.borderColor = 'var(--caramel)'
              }}
              {...register('password')}
              onBlur={e => {
                if (!errors.password) e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)'
              }}
            />
            {errors.password && (
              <p className="mt-1 font-mono text-xs" style={{ color: 'var(--crimson)' }}>
                {errors.password.message}
              </p>
            )}
          </div>

          {/* Server error */}
          {error && (
            <div
              className="px-4 py-3 border"
              style={{
                background: 'rgba(139,26,26,0.2)',
                borderColor: 'var(--crimson)',
              }}
            >
              <p className="font-mono text-xs" style={{ color: '#fca5a5' }}>{error}</p>
            </div>
          )}

          {/* Submit */}
          <button
            type="submit"
            disabled={isLoading}
            className="w-full py-3 font-caps text-sm tracking-widest transition-opacity"
            style={{
              background: 'var(--crimson)',
              color: '#E8E8E8',
              opacity: isLoading ? 0.7 : 1,
            }}
          >
            {isLoading ? 'SIGNING IN...' : 'SIGN IN'}
          </button>
        </form>
      </div>

      {/* Register link */}
      <div className="mt-6 text-center">
        <p className="font-mono text-xs" style={{ color: '#9ca3af' }}>
          Don't have an account?{' '}
          <Link
            to="/register"
            className="transition-colors"
            style={{ color: 'var(--caramel)' }}
            onMouseEnter={e => (e.currentTarget.style.color = '#E8E8E8')}
            onMouseLeave={e => (e.currentTarget.style.color = 'var(--caramel)')}
          >
            Register
          </Link>
        </p>
      </div>
    </div>
  )
}
