import { useState } from 'react'
import { Plus, Settings, Trash2, Search } from 'lucide-react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useUsers } from './useUsers'
import { SlideOver } from '@/components/shared/SlideOver'
import { ConfirmDialog } from '@/components/shared/ConfirmDialog'
import type { UserDto } from '@/api/generated'

const createSchema = z.object({
  firstName: z.string().min(1, 'First name is required'),
  lastName: z.string().min(1, 'Last name is required'),
  email: z.string().min(1, 'Email is required').email('Invalid email'),
  password: z.string().min(8, 'Password must be at least 8 characters'),
  role: z.enum(['Editor', 'Admin']),
})
type CreateFormData = z.infer<typeof createSchema>

const editSchema = z.object({
  firstName: z.string().min(1, 'First name is required'),
  lastName: z.string().min(1, 'Last name is required'),
  email: z.string().min(1, 'Email is required').email('Invalid email'),
})
type EditFormData = z.infer<typeof editSchema>

const inputClass =
  'w-full px-4 py-3 font-mono text-sm text-gray-200 placeholder-gray-600 focus:outline-none transition-colors'
const inputStyle = {
  background: 'var(--near-black)',
  border: '1px solid rgba(255,255,255,0.1)',
}

function FieldLabel({ children }: { children: React.ReactNode }) {
  return (
    <label className="block font-caps text-xs tracking-widest mb-2" style={{ color: 'var(--caramel)' }}>
      {children}
    </label>
  )
}

function FieldError({ message }: { message?: string }) {
  if (!message) return null
  return <p className="mt-1 font-mono text-xs" style={{ color: 'var(--crimson)' }}>{message}</p>
}

const roleBadgeStyle: Record<string, string> = {
  Admin: 'border-[var(--crimson)] text-[var(--crimson)]',
  Editor: 'border-[var(--caramel)] text-[var(--caramel)]',
}

export function UsersPage() {
  const [slideMode, setSlideMode] = useState<'create' | 'edit' | null>(null)
  const [editTarget, setEditTarget] = useState<UserDto | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<UserDto | null>(null)
  const [search, setSearch] = useState('')

  const { createEditor, updateEditor, deleteEditor, usersQuery } = useUsers({
    onCreated: () => { setSlideMode(null); createReset() },
    onUpdated: () => { setSlideMode(null); setEditTarget(null); editReset() },
    onDeleted: () => setDeleteTarget(null),
  })

  const {
    register: createRegister,
    handleSubmit: createHandleSubmit,
    reset: createReset,
    formState: { errors: createErrors },
  } = useForm<CreateFormData>({ resolver: zodResolver(createSchema), defaultValues: { role: 'Editor' } })

  const {
    register: editRegister,
    handleSubmit: editHandleSubmit,
    reset: editReset,
    formState: { errors: editErrors },
  } = useForm<EditFormData>({ resolver: zodResolver(editSchema) })

  const openEdit = (user: UserDto) => {
    setEditTarget(user)
    editReset({ firstName: user.firstName ?? '', lastName: user.lastName ?? '', email: user.email ?? '' })
    setSlideMode('edit')
  }

  const openCreate = () => {
    createReset()
    setSlideMode('create')
  }

  const closeSlide = () => {
    setSlideMode(null)
    setEditTarget(null)
  }

  const onCreateSubmit = (data: CreateFormData) => createEditor.mutate(data)
  const onEditSubmit = (data: EditFormData) => {
    if (!editTarget?.id) return
    updateEditor.mutate({ id: editTarget.id, ...data })
  }

  const users = usersQuery.data ?? []
  const filtered = users.filter(u => {
    const q = search.toLowerCase()
    return !q || u.firstName?.toLowerCase().includes(q) || u.lastName?.toLowerCase().includes(q) || u.email?.toLowerCase().includes(q)
  })

  const isPending = createEditor.isPending || updateEditor.isPending

  return (
    <div className="p-8">
      {/* Page Header */}
      <div className="flex items-start justify-between mb-8">
        <div>
          <h1 className="font-display text-5xl text-white mb-2">User Directory</h1>
          <p className="font-mono text-sm text-gray-400">
            {usersQuery.isLoading ? 'Loading…' : `${users.length} user${users.length !== 1 ? 's' : ''} registered`}
          </p>
        </div>
        <button
          onClick={openCreate}
          className="flex items-center gap-2 px-4 py-2.5 font-caps text-xs tracking-wider text-white transition-colors"
          style={{ background: 'var(--crimson)' }}
          onMouseEnter={e => (e.currentTarget.style.background = 'rgba(139,26,26,0.8)')}
          onMouseLeave={e => (e.currentTarget.style.background = 'var(--crimson)')}
        >
          <Plus className="w-4 h-4" />
          ADD USER
        </button>
      </div>

      {/* Search */}
      <div className="mb-6">
        <div className="relative max-w-sm">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500 pointer-events-none" />
          <input
            type="text"
            placeholder="Search users..."
            value={search}
            onChange={e => setSearch(e.target.value)}
            className="w-full pl-9 pr-3 py-2.5 font-mono text-sm text-gray-300 placeholder-gray-600 focus:outline-none transition-colors"
            style={{ background: 'var(--near-black)', border: '1px solid rgba(255,255,255,0.1)' }}
            onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
            onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
          />
        </div>
      </div>

      {/* Table */}
      <div style={{ border: '1px solid rgba(255,255,255,0.1)' }}>
        {/* Header */}
        <div
          className="grid grid-cols-12 px-4 py-3"
          style={{ background: 'var(--burgundy)', borderBottom: '1px solid rgba(255,255,255,0.1)' }}
        >
          <div className="col-span-1 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>ID</div>
          <div className="col-span-3 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>NAME</div>
          <div className="col-span-5 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>EMAIL</div>
          <div className="col-span-2 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>ROLE</div>
          <div className="col-span-1" />
        </div>

        {/* Rows */}
        {usersQuery.isLoading ? (
          <div className="px-4 py-12 text-center font-mono text-sm text-gray-500">Loading users…</div>
        ) : filtered.length === 0 ? (
          <div className="px-4 py-12 text-center font-mono text-sm text-gray-500">
            {search ? 'No users match your search.' : 'No users registered yet.'}
          </div>
        ) : (
          filtered.map((user, i) => (
            <div
              key={user.id ?? user.email ?? i}
              className="group grid grid-cols-12 px-4 py-4 items-center transition-colors"
              style={{ borderBottom: i < filtered.length - 1 ? '1px solid rgba(255,255,255,0.06)' : undefined }}
              onMouseEnter={e => (e.currentTarget.style.background = 'rgba(61,15,15,0.3)')}
              onMouseLeave={e => (e.currentTarget.style.background = '')}
            >
              <div className="col-span-1 font-mono text-sm text-gray-500 truncate">
                {user.id?.slice(0, 6) ?? '—'}
              </div>
              <div className="col-span-3 font-mono text-sm text-white">
                {user.firstName} {user.lastName}
              </div>
              <div className="col-span-5 font-mono text-sm text-gray-400 truncate">
                {user.email}
              </div>
              <div className="col-span-2">
                <span
                  className={`inline-block px-2 py-0.5 font-caps text-xs border ${roleBadgeStyle[user.role ?? ''] ?? 'border-gray-600 text-gray-500'}`}
                >
                  {user.role ?? '—'}
                </span>
              </div>
              <div
                className="col-span-1 flex items-center gap-2 justify-end opacity-0 group-hover:opacity-100 transition-opacity"
                onClick={e => e.stopPropagation()}
              >
                {user.role !== 'Admin' && (
                  <>
                    <button
                      onClick={() => openEdit(user)}
                      className="p-1.5 transition-colors"
                      style={{ color: 'var(--caramel)' }}
                      title="Edit"
                    >
                      <Settings className="w-4 h-4" />
                    </button>
                    <button
                      onClick={() => setDeleteTarget(user)}
                      className="p-1.5 transition-colors"
                      style={{ color: 'var(--crimson)' }}
                      title="Delete"
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </>
                )}
              </div>
            </div>
          ))
        )}
      </div>

      {/* Create SlideOver */}
      <SlideOver isOpen={slideMode === 'create'} onClose={closeSlide} title="ADD USER">
        <form onSubmit={createHandleSubmit(onCreateSubmit)} className="flex flex-col h-full">
          <div className="flex-1 px-6 py-6 space-y-5">
            <div className="grid grid-cols-2 gap-3">
              <div>
                <FieldLabel>FIRST NAME</FieldLabel>
                <input
                  className={inputClass}
                  style={inputStyle}
                  placeholder="First name"
                  onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                  onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
                  {...createRegister('firstName')}
                />
                <FieldError message={createErrors.firstName?.message} />
              </div>
              <div>
                <FieldLabel>LAST NAME</FieldLabel>
                <input
                  className={inputClass}
                  style={inputStyle}
                  placeholder="Last name"
                  onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                  onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
                  {...createRegister('lastName')}
                />
                <FieldError message={createErrors.lastName?.message} />
              </div>
            </div>
            <div>
              <FieldLabel>EMAIL</FieldLabel>
              <input
                type="email"
                className={inputClass}
                style={inputStyle}
                placeholder="user@example.com"
                onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
                {...createRegister('email')}
              />
              <FieldError message={createErrors.email?.message} />
            </div>
            <div>
              <FieldLabel>PASSWORD</FieldLabel>
              <input
                type="password"
                className={inputClass}
                style={inputStyle}
                placeholder="Min. 8 characters"
                onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
                {...createRegister('password')}
              />
              <FieldError message={createErrors.password?.message} />
            </div>
            <div>
              <FieldLabel>ROLE</FieldLabel>
              <select
                className={inputClass}
                style={inputStyle}
                onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
                {...createRegister('role')}
              >
                <option value="Editor">Editor</option>
                <option value="Admin">Admin</option>
              </select>
              <FieldError message={createErrors.role?.message} />
            </div>
          </div>
          <div className="px-6 py-4 flex flex-col gap-3" style={{ borderTop: '1px solid rgba(255,255,255,0.1)' }}>
            <button
              type="submit"
              disabled={isPending}
              className="w-full py-3 font-caps text-sm tracking-wider text-white transition-colors disabled:opacity-50"
              style={{ background: 'var(--crimson)' }}
            >
              {createEditor.isPending ? 'CREATING…' : 'CREATE USER'}
            </button>
            <button
              type="button"
              onClick={closeSlide}
              disabled={isPending}
              className="w-full py-3 font-caps text-sm tracking-wider text-gray-400 transition-colors disabled:opacity-50"
              style={{ border: '1px solid rgba(255,255,255,0.2)' }}
              onMouseEnter={e => { e.currentTarget.style.borderColor = 'var(--caramel)'; e.currentTarget.style.color = 'var(--caramel)' }}
              onMouseLeave={e => { e.currentTarget.style.borderColor = 'rgba(255,255,255,0.2)'; e.currentTarget.style.color = '#9ca3af' }}
            >
              CANCEL
            </button>
          </div>
        </form>
      </SlideOver>

      {/* Edit SlideOver */}
      <SlideOver isOpen={slideMode === 'edit'} onClose={closeSlide} title="EDIT USER">
        <form onSubmit={editHandleSubmit(onEditSubmit)} className="flex flex-col h-full">
          <div className="flex-1 px-6 py-6 space-y-5">
            <div className="grid grid-cols-2 gap-3">
              <div>
                <FieldLabel>FIRST NAME</FieldLabel>
                <input
                  className={inputClass}
                  style={inputStyle}
                  placeholder="First name"
                  onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                  onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
                  {...editRegister('firstName')}
                />
                <FieldError message={editErrors.firstName?.message} />
              </div>
              <div>
                <FieldLabel>LAST NAME</FieldLabel>
                <input
                  className={inputClass}
                  style={inputStyle}
                  placeholder="Last name"
                  onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                  onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
                  {...editRegister('lastName')}
                />
                <FieldError message={editErrors.lastName?.message} />
              </div>
            </div>
            <div>
              <FieldLabel>EMAIL</FieldLabel>
              <input
                type="email"
                className={inputClass}
                style={inputStyle}
                placeholder="user@example.com"
                onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
                {...editRegister('email')}
              />
              <FieldError message={editErrors.email?.message} />
            </div>
          </div>
          <div className="px-6 py-4 flex flex-col gap-3" style={{ borderTop: '1px solid rgba(255,255,255,0.1)' }}>
            <button
              type="submit"
              disabled={isPending}
              className="w-full py-3 font-caps text-sm tracking-wider text-white transition-colors disabled:opacity-50"
              style={{ background: 'var(--crimson)' }}
            >
              {updateEditor.isPending ? 'SAVING…' : 'SAVE CHANGES'}
            </button>
            <button
              type="button"
              onClick={closeSlide}
              disabled={isPending}
              className="w-full py-3 font-caps text-sm tracking-wider text-gray-400 transition-colors disabled:opacity-50"
              style={{ border: '1px solid rgba(255,255,255,0.2)' }}
              onMouseEnter={e => { e.currentTarget.style.borderColor = 'var(--caramel)'; e.currentTarget.style.color = 'var(--caramel)' }}
              onMouseLeave={e => { e.currentTarget.style.borderColor = 'rgba(255,255,255,0.2)'; e.currentTarget.style.color = '#9ca3af' }}
            >
              CANCEL
            </button>
          </div>
        </form>
      </SlideOver>

      <ConfirmDialog
        isOpen={deleteTarget !== null}
        onClose={() => setDeleteTarget(null)}
        onConfirm={() => { if (deleteTarget?.id) deleteEditor.mutate(deleteTarget.id) }}
        title="Delete User"
        message={`Are you sure you want to delete ${deleteTarget?.firstName} ${deleteTarget?.lastName}? This action cannot be undone.`}
        confirmLabel="Delete"
        variant="danger"
        isLoading={deleteEditor.isPending}
      />
    </div>
  )
}
