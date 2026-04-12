import { useEffect } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useAuthStore } from '@/store/authStore'
import { useRegister } from './useRegister'

const schema = z.object({
  email: z.string().min(1, 'Email is required').email('Invalid email address'),
  firstName: z.string().min(1, 'First name is required'),
  lastName: z.string().min(1, 'Last name is required'),
  password: z.string().min(6, 'Password must be at least 6 characters'),
  confirmPassword: z.string().min(1, 'Please confirm your password'),
}).refine(data => data.password === data.confirmPassword, {
  message: 'Passwords do not match',
  path: ['confirmPassword'],
})

type FormData = z.infer<typeof schema>

export function RegisterPage() {
  const isAuthenticated = useAuthStore(state => state.isAuthenticated)
  const navigate = useNavigate()
  const { register: registerUser, error, isLoading } = useRegister()

  useEffect(() => {
    if (isAuthenticated()) navigate('/articles', { replace: true })
  }, [isAuthenticated, navigate])

  const { register, handleSubmit, formState: { errors } } = useForm<FormData>({
    resolver: zodResolver(schema),
  })

  const onSubmit = (data: FormData) =>
    registerUser(data.email, data.firstName, data.lastName, data.password)

  const fieldStyle = (hasError: boolean) => ({
    background: 'var(--burgundy)',
    borderColor: hasError ? 'var(--crimson)' : 'rgba(255,255,255,0.1)',
    color: '#E8E8E8',
  })

  const handleFocus = (e: React.FocusEvent<HTMLInputElement>, hasError: boolean) => {
    if (!hasError) e.currentTarget.style.borderColor = 'var(--caramel)'
  }

  const handleBlur = (e: React.FocusEvent<HTMLInputElement>, hasError: boolean) => {
    if (!hasError) e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)'
  }

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
            EDITOR REGISTRATION
          </div>
          <p className="font-mono text-sm" style={{ color: '#9ca3af' }}>
            Create your editor account
          </p>
        </div>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
          {/* Name row */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block font-caps text-xs tracking-widest mb-2" style={{ color: 'var(--caramel)' }}>
                FIRST NAME
              </label>
              <input
                type="text"
                placeholder="John"
                className="w-full px-4 py-3 font-mono text-sm border focus:outline-none transition-colors"
                style={fieldStyle(!!errors.firstName)}
                onFocus={e => handleFocus(e, !!errors.firstName)}
                {...register('firstName')}
                onBlur={e => handleBlur(e, !!errors.firstName)}
              />
              {errors.firstName && (
                <p className="mt-1 font-mono text-xs" style={{ color: 'var(--crimson)' }}>
                  {errors.firstName.message}
                </p>
              )}
            </div>
            <div>
              <label className="block font-caps text-xs tracking-widest mb-2" style={{ color: 'var(--caramel)' }}>
                LAST NAME
              </label>
              <input
                type="text"
                placeholder="Doe"
                className="w-full px-4 py-3 font-mono text-sm border focus:outline-none transition-colors"
                style={fieldStyle(!!errors.lastName)}
                onFocus={e => handleFocus(e, !!errors.lastName)}
                {...register('lastName')}
                onBlur={e => handleBlur(e, !!errors.lastName)}
              />
              {errors.lastName && (
                <p className="mt-1 font-mono text-xs" style={{ color: 'var(--crimson)' }}>
                  {errors.lastName.message}
                </p>
              )}
            </div>
          </div>

          {/* Email */}
          <div>
            <label className="block font-caps text-xs tracking-widest mb-2" style={{ color: 'var(--caramel)' }}>
              EMAIL
            </label>
            <input
              type="email"
              placeholder="you@example.com"
              className="w-full px-4 py-3 font-mono text-sm border focus:outline-none transition-colors"
              style={fieldStyle(!!errors.email)}
              onFocus={e => handleFocus(e, !!errors.email)}
              {...register('email')}
              onBlur={e => handleBlur(e, !!errors.email)}
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
              style={fieldStyle(!!errors.password)}
              onFocus={e => handleFocus(e, !!errors.password)}
              {...register('password')}
              onBlur={e => handleBlur(e, !!errors.password)}
            />
            {errors.password && (
              <p className="mt-1 font-mono text-xs" style={{ color: 'var(--crimson)' }}>
                {errors.password.message}
              </p>
            )}
          </div>

          {/* Confirm Password */}
          <div>
            <label className="block font-caps text-xs tracking-widest mb-2" style={{ color: 'var(--caramel)' }}>
              CONFIRM PASSWORD
            </label>
            <input
              type="password"
              placeholder="••••••••"
              className="w-full px-4 py-3 font-mono text-sm border focus:outline-none transition-colors"
              style={fieldStyle(!!errors.confirmPassword)}
              onFocus={e => handleFocus(e, !!errors.confirmPassword)}
              {...register('confirmPassword')}
              onBlur={e => handleBlur(e, !!errors.confirmPassword)}
            />
            {errors.confirmPassword && (
              <p className="mt-1 font-mono text-xs" style={{ color: 'var(--crimson)' }}>
                {errors.confirmPassword.message}
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
            {isLoading ? 'CREATING ACCOUNT...' : 'CREATE ACCOUNT'}
          </button>
        </form>
      </div>

      {/* Sign in link */}
      <div className="mt-6 text-center">
        <p className="font-mono text-xs" style={{ color: '#9ca3af' }}>
          Already have an account?{' '}
          <Link
            to="/login"
            className="transition-colors"
            style={{ color: 'var(--caramel)' }}
            onMouseEnter={e => (e.currentTarget.style.color = '#E8E8E8')}
            onMouseLeave={e => (e.currentTarget.style.color = 'var(--caramel)')}
          >
            Sign in
          </Link>
        </p>
      </div>
    </div>
  )
}
