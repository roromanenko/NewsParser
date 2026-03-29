import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import type { PublishTargetDto } from '@/api/generated'
import { SlideOver } from '@/components/shared/SlideOver'
import { usePublishTargetMutations } from './usePublishTargetMutations'

const inputClass =
  'w-full px-4 py-3 font-mono text-sm text-gray-200 placeholder-gray-600 focus:outline-none transition-colors'
const inputStyle = {
  background: 'var(--near-black)',
  border: '1px solid rgba(255,255,255,0.1)',
}

interface Props {
  isOpen: boolean
  onClose: () => void
  target?: PublishTargetDto
}

const createSchema = z.object({
  name: z.string().min(1, 'Name is required'),
  platform: z.enum(['Telegram', 'Website'], { message: 'Select a platform' }),
  identifier: z.string().min(1, 'Identifier is required'),
  systemPrompt: z.string().min(1, 'System prompt is required'),
})

const editSchema = z.object({
  name: z.string().min(1, 'Name is required'),
  identifier: z.string().min(1, 'Identifier is required'),
  systemPrompt: z.string().min(1, 'System prompt is required'),
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

export function PublishTargetFormSlideOver({ isOpen, onClose, target }: Props) {
  const { createTarget, updateTarget } = usePublishTargetMutations()
  const isEdit = !!target

  const createForm = useForm<CreateData>({ resolver: zodResolver(createSchema) })
  const editForm = useForm<EditData>({
    resolver: zodResolver(editSchema),
    defaultValues: {
      name: target?.name ?? '',
      identifier: target?.identifier ?? '',
      systemPrompt: target?.systemPrompt ?? '',
      isActive: target?.isActive ?? true,
    },
  })

  useEffect(() => {
    if (target) {
      editForm.reset({
        name: target.name ?? '',
        identifier: target.identifier ?? '',
        systemPrompt: target.systemPrompt ?? '',
        isActive: target.isActive ?? true,
      })
    } else {
      createForm.reset()
    }
  }, [target, isOpen])

  const onCreateSubmit = async (data: CreateData) => {
    await createTarget.mutateAsync(data)
    onClose()
  }

  const onEditSubmit = async (data: EditData) => {
    if (!target?.id) return
    await updateTarget.mutateAsync({ id: target.id, data })
    onClose()
  }

  const isPending = createTarget.isPending || updateTarget.isPending

  return (
    <SlideOver isOpen={isOpen} onClose={onClose} title={isEdit ? 'EDIT TARGET' : 'ADD TARGET'}>
      {isEdit ? (
        <form onSubmit={editForm.handleSubmit(onEditSubmit)} className="flex flex-col h-full">
          <div className="flex-1 px-6 py-6 space-y-5">
            <div>
              <FieldLabel>NAME</FieldLabel>
              <input
                className={inputClass}
                style={inputStyle}
                placeholder="Target name"
                onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
                {...editForm.register('name')}
              />
              <FieldError message={editForm.formState.errors.name?.message} />
            </div>
            <div>
              <FieldLabel>IDENTIFIER</FieldLabel>
              <input
                className={inputClass}
                style={inputStyle}
                placeholder="e.g. @channel"
                onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
                {...editForm.register('identifier')}
              />
              <FieldError message={editForm.formState.errors.identifier?.message} />
            </div>
            <div>
              <FieldLabel>SYSTEM PROMPT</FieldLabel>
              <textarea
                rows={6}
                className={`${inputClass} resize-none`}
                style={inputStyle}
                placeholder="Enter system prompt…"
                onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
                {...editForm.register('systemPrompt')}
              />
              <FieldError message={editForm.formState.errors.systemPrompt?.message} />
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
          <div className="px-6 py-4 flex flex-col gap-3" style={{ borderTop: '1px solid rgba(255,255,255,0.1)' }}>
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
                placeholder="Target name"
                onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
                {...createForm.register('name')}
              />
              <FieldError message={createForm.formState.errors.name?.message} />
            </div>
            <div>
              <FieldLabel>PLATFORM</FieldLabel>
              <select
                className={inputClass}
                style={inputStyle}
                onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
                {...createForm.register('platform')}
              >
                <option value="">Select platform…</option>
                <option value="Telegram">Telegram</option>
                <option value="Website">Website</option>
              </select>
              <FieldError message={createForm.formState.errors.platform?.message} />
            </div>
            <div>
              <FieldLabel>IDENTIFIER</FieldLabel>
              <input
                className={inputClass}
                style={inputStyle}
                placeholder="e.g. @channel"
                onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
                {...createForm.register('identifier')}
              />
              <FieldError message={createForm.formState.errors.identifier?.message} />
            </div>
            <div>
              <FieldLabel>SYSTEM PROMPT</FieldLabel>
              <textarea
                rows={6}
                className={`${inputClass} resize-none`}
                style={inputStyle}
                placeholder="Enter system prompt…"
                onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
                onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
                {...createForm.register('systemPrompt')}
              />
              <FieldError message={createForm.formState.errors.systemPrompt?.message} />
            </div>
          </div>
          <div className="px-6 py-4 flex flex-col gap-3" style={{ borderTop: '1px solid rgba(255,255,255,0.1)' }}>
            <button
              type="submit"
              disabled={isPending}
              className="w-full py-3 font-caps text-sm tracking-wider text-white transition-colors disabled:opacity-50"
              style={{ background: 'var(--crimson)' }}
            >
              {isPending ? 'ADDING…' : 'ADD TARGET'}
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
