import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import type { SourceDto } from '@/api/generated'
import { SlideOver } from '@/components/shared/SlideOver'
import { useSourceMutations } from './useSourceMutations'

const inputClass =
  'w-full px-4 py-3 font-mono text-sm text-gray-200 placeholder-gray-600 focus:outline-none transition-colors'
const inputStyle = {
  background: 'var(--near-black)',
  border: '1px solid rgba(255,255,255,0.1)',
}

interface SourceFormSlideOverProps {
  isOpen: boolean
  onClose: () => void
  source?: SourceDto
}

const createSchema = z.object({
  name: z.string().min(1, 'Name is required'),
  url: z.string().min(1, 'URL is required').url('Must be a valid URL'),
  type: z.enum(['Rss', 'Telegram'], { message: 'Select a source type' }),
})
const editSchema = z.object({
  name: z.string().min(1, 'Name is required'),
  url: z.string().min(1, 'URL is required').url('Must be a valid URL'),
  isActive: z.boolean(),
})
type CreateData = z.infer<typeof createSchema>
type EditData = z.infer<typeof editSchema>

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

export function SourceFormSlideOver({ isOpen, onClose, source }: SourceFormSlideOverProps) {
  const { createSource, updateSource } = useSourceMutations()
  const isEdit = !!source

  const createForm = useForm<CreateData>({ resolver: zodResolver(createSchema) })
  const editForm = useForm<EditData>({
    resolver: zodResolver(editSchema),
    defaultValues: { name: source?.name ?? '', url: source?.url ?? '', isActive: source?.isActive ?? true },
  })

  useEffect(() => {
    if (source) {
      editForm.reset({ name: source.name ?? '', url: source.url ?? '', isActive: source.isActive ?? true })
    } else {
      createForm.reset()
    }
  }, [source, isOpen])

  const onCreateSubmit = async (data: CreateData) => {
    await createSource.mutateAsync(data)
    onClose()
  }
  const onEditSubmit = async (data: EditData) => {
    if (!source?.id) return
    await updateSource.mutateAsync({ id: source.id, data })
    onClose()
  }

  const isPending = createSource.isPending || updateSource.isPending

  return (
    <SlideOver isOpen={isOpen} onClose={onClose} title={isEdit ? 'EDIT SOURCE' : 'ADD SOURCE'}>
      {isEdit ? (
        <form onSubmit={editForm.handleSubmit(onEditSubmit)} className="flex flex-col h-full">
          <div className="flex-1 px-6 py-6 space-y-5">
            <div>
              <FieldLabel>NAME</FieldLabel>
              <input
                className={inputClass}
                style={inputStyle}
                placeholder="Source name"
                onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                {...editForm.register('name')}
                onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
              />
              <FieldError message={editForm.formState.errors.name?.message} />
            </div>
            <div>
              <FieldLabel>URL</FieldLabel>
              <input
                type="url"
                className={inputClass}
                style={inputStyle}
                placeholder="https://..."
                onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                {...editForm.register('url')}
                onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
              />
              <FieldError message={editForm.formState.errors.url?.message} />
            </div>
            <div>
              <FieldLabel>STATUS</FieldLabel>
              <label className="flex items-center gap-3 cursor-pointer">
                <input
                  type="checkbox"
                  className="w-4 h-4 accent-[var(--caramel)]"
                  {...editForm.register('isActive')}
                />
                <span className="font-mono text-sm text-gray-300">Active</span>
              </label>
            </div>
          </div>
          <div
            className="px-6 py-4 flex flex-col gap-3"
            style={{ borderTop: '1px solid rgba(255,255,255,0.1)' }}
          >
            <button
              type="submit"
              disabled={isPending}
              className="w-full py-3 font-caps text-sm tracking-wider text-white transition-colors disabled:opacity-50"
              style={{ background: 'var(--crimson)' }}
            >
              {isPending ? 'SAVING…' : 'SAVE CHANGES'}
            </button>
            <button
              type="button"
              onClick={onClose}
              disabled={isPending}
              className="w-full py-3 font-caps text-sm tracking-wider text-gray-400 transition-colors disabled:opacity-50"
              style={{ border: '1px solid rgba(255,255,255,0.2)' }}
              onMouseEnter={e => {
                e.currentTarget.style.borderColor = 'var(--caramel)'
                e.currentTarget.style.color = 'var(--caramel)'
              }}
              onMouseLeave={e => {
                e.currentTarget.style.borderColor = 'rgba(255,255,255,0.2)'
                e.currentTarget.style.color = '#9ca3af'
              }}
            >
              CANCEL
            </button>
          </div>
        </form>
      ) : (
        <form onSubmit={createForm.handleSubmit(onCreateSubmit)} className="flex flex-col h-full">
          <div className="flex-1 px-6 py-6 space-y-5">
            <div>
              <FieldLabel>NAME</FieldLabel>
              <input
                className={inputClass}
                style={inputStyle}
                placeholder="Source name"
                onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                {...createForm.register('name')}
                onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
              />
              <FieldError message={createForm.formState.errors.name?.message} />
            </div>
            <div>
              <FieldLabel>URL</FieldLabel>
              <input
                type="url"
                className={inputClass}
                style={inputStyle}
                placeholder="https://..."
                onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                {...createForm.register('url')}
                onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
              />
              <FieldError message={createForm.formState.errors.url?.message} />
            </div>
            <div>
              <FieldLabel>TYPE</FieldLabel>
              <select
                className={inputClass}
                style={inputStyle}
                onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                {...createForm.register('type')}
                onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
              >
                <option value="">Select type…</option>
                <option value="Rss">RSS</option>
                <option value="Telegram">Telegram</option>
              </select>
              <FieldError message={createForm.formState.errors.type?.message} />
            </div>
          </div>
          <div
            className="px-6 py-4 flex flex-col gap-3"
            style={{ borderTop: '1px solid rgba(255,255,255,0.1)' }}
          >
            <button
              type="submit"
              disabled={isPending}
              className="w-full py-3 font-caps text-sm tracking-wider text-white transition-colors disabled:opacity-50"
              style={{ background: 'var(--crimson)' }}
            >
              {isPending ? 'ADDING…' : 'ADD TO REGISTRY'}
            </button>
            <button
              type="button"
              onClick={onClose}
              disabled={isPending}
              className="w-full py-3 font-caps text-sm tracking-wider text-gray-400 transition-colors disabled:opacity-50"
              style={{ border: '1px solid rgba(255,255,255,0.2)' }}
              onMouseEnter={e => {
                e.currentTarget.style.borderColor = 'var(--caramel)'
                e.currentTarget.style.color = 'var(--caramel)'
              }}
              onMouseLeave={e => {
                e.currentTarget.style.borderColor = 'rgba(255,255,255,0.2)'
                e.currentTarget.style.color = '#9ca3af'
              }}
            >
              CANCEL
            </button>
          </div>
        </form>
      )}
    </SlideOver>
  )
}
