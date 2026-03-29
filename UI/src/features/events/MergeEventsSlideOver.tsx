import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { SlideOver } from '@/components/shared/SlideOver'
import { Button } from '@/components/ui/Button'
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
    <SlideOver isOpen={isOpen} onClose={onClose} title="Merge Events">
      <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-5 p-6">
        <p className="text-sm text-gray-500">
          Articles from the source event will be moved into the target event. The source event will be removed.
        </p>

        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Source Event</label>
          <select
            {...register('sourceEventId')}
            className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
          >
            <option value="">Select source event…</option>
            {events.map(e => (
              <option key={e.id} value={e.id}>
                {e.title}
              </option>
            ))}
          </select>
          {errors.sourceEventId && (
            <p className="mt-1 text-xs text-red-600">{errors.sourceEventId.message}</p>
          )}
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Target Event</label>
          <select
            {...register('targetEventId')}
            className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
          >
            <option value="">Select target event…</option>
            {events.map(e => (
              <option key={e.id} value={e.id}>
                {e.title}
              </option>
            ))}
          </select>
          {errors.targetEventId && (
            <p className="mt-1 text-xs text-red-600">{errors.targetEventId.message}</p>
          )}
        </div>

        <div className="flex justify-end gap-3 pt-2">
          <Button type="button" variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" variant="danger" isLoading={mergeEvents.isPending}>
            Merge
          </Button>
        </div>
      </form>
    </SlideOver>
  )
}
