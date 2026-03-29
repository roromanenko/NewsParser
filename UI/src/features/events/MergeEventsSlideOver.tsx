import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { SlideOver } from '@/components/shared/SlideOver'
import { useEventMutations } from './useEventMutations'
import type { EventListItemDto } from '@/api/generated'

const schema = z
  .object({
    sourceEventId: z.string().min(1, 'Select a source event'),
    targetEventId: z.string().min(1, 'Select a target event'),
  })
  .refine(d => d.sourceEventId !== d.targetEventId, {
    message: 'Source and target must be different events',
    path: ['targetEventId'],
  })

type FormData = z.infer<typeof schema>

interface Props {
  isOpen: boolean
  onClose: () => void
  events: EventListItemDto[]
}

export function MergeEventsSlideOver({ isOpen, onClose, events }: Props) {
  const { mergeEvents } = useEventMutations()

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<FormData>({ resolver: zodResolver(schema) })

  useEffect(() => {
    if (!isOpen) reset()
  }, [isOpen, reset])

  const onSubmit = (data: FormData) => {
    mergeEvents.mutate(data, { onSuccess: onClose })
  }

  return (
    <SlideOver isOpen={isOpen} onClose={onClose} title="MERGE EVENTS">
      <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-6 p-6">
        <p className="font-mono text-sm leading-relaxed" style={{ color: '#9ca3af' }}>
          Articles from the source event will be moved into the target event. The source event will be removed.
        </p>

        <div>
          <label
            className="block font-caps text-xs tracking-widest mb-2"
            style={{ color: 'var(--caramel)' }}
          >
            SOURCE EVENT
          </label>
          <select
            {...register('sourceEventId')}
            className="w-full px-3 py-2 font-mono text-sm focus:outline-none transition-colors"
            style={{
              background: 'var(--near-black)',
              border: '1px solid rgba(255,255,255,0.1)',
              color: '#E8E8E8',
            }}
            onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
            onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
          >
            <option value="" style={{ background: 'var(--near-black)' }}>Select source event…</option>
            {events.map(e => (
              <option key={e.id} value={e.id} style={{ background: 'var(--near-black)' }}>
                {e.title}
              </option>
            ))}
          </select>
          {errors.sourceEventId && (
            <p className="mt-1 font-mono text-xs" style={{ color: 'var(--rust)' }}>
              {errors.sourceEventId.message}
            </p>
          )}
        </div>

        <div>
          <label
            className="block font-caps text-xs tracking-widest mb-2"
            style={{ color: 'var(--caramel)' }}
          >
            TARGET EVENT
          </label>
          <select
            {...register('targetEventId')}
            className="w-full px-3 py-2 font-mono text-sm focus:outline-none transition-colors"
            style={{
              background: 'var(--near-black)',
              border: '1px solid rgba(255,255,255,0.1)',
              color: '#E8E8E8',
            }}
            onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
            onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
          >
            <option value="" style={{ background: 'var(--near-black)' }}>Select target event…</option>
            {events.map(e => (
              <option key={e.id} value={e.id} style={{ background: 'var(--near-black)' }}>
                {e.title}
              </option>
            ))}
          </select>
          {errors.targetEventId && (
            <p className="mt-1 font-mono text-xs" style={{ color: 'var(--rust)' }}>
              {errors.targetEventId.message}
            </p>
          )}
        </div>

        <div
          className="flex justify-end gap-3 pt-4 border-t"
          style={{ borderColor: 'rgba(255,255,255,0.1)' }}
        >
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 font-caps text-xs tracking-wider border transition-colors"
            style={{ borderColor: 'rgba(255,255,255,0.2)', color: '#9ca3af' }}
            onMouseEnter={e => {
              e.currentTarget.style.borderColor = 'rgba(255,255,255,0.4)'
              e.currentTarget.style.color = '#E8E8E8'
            }}
            onMouseLeave={e => {
              e.currentTarget.style.borderColor = 'rgba(255,255,255,0.2)'
              e.currentTarget.style.color = '#9ca3af'
            }}
          >
            CANCEL
          </button>
          <button
            type="submit"
            disabled={mergeEvents.isPending}
            className="px-4 py-2 font-caps text-xs tracking-wider text-white transition-opacity hover:opacity-90 disabled:opacity-50"
            style={{ background: 'var(--crimson)' }}
          >
            {mergeEvents.isPending ? 'MERGING…' : 'MERGE'}
          </button>
        </div>
      </form>
    </SlideOver>
  )
}
