import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Textarea'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useEventMutations } from './useEventMutations'

interface RejectEventModalProps {
  isOpen: boolean
  onClose: () => void
  eventId: string
}

const schema = z.object({
  reason: z.string().min(1, 'Rejection reason is required').max(500, 'Max 500 characters'),
})
type FormData = z.infer<typeof schema>

export function RejectEventModal({ isOpen, onClose, eventId }: RejectEventModalProps) {
  const { rejectEvent } = useEventMutations(eventId)

  const { register, handleSubmit, reset, formState: { errors } } = useForm<FormData>({
    resolver: zodResolver(schema),
  })

  async function onSubmit(data: FormData) {
    await rejectEvent.mutateAsync(data.reason)
    reset()
    onClose()
  }

  return (
    <Modal isOpen={isOpen} onClose={onClose} title="Reject Event" maxWidth="sm">
      <form onSubmit={handleSubmit(onSubmit)}>
        <div className="px-6 py-4">
          <Textarea
            label="Rejection reason"
            placeholder="Explain why this event is being rejected..."
            error={errors.reason?.message}
            rows={4}
            {...register('reason')}
          />
        </div>
        <div className="flex justify-end gap-3 border-t border-gray-200 px-6 py-4">
          <Button variant="secondary" onClick={onClose} type="button" disabled={rejectEvent.isPending}>
            Cancel
          </Button>
          <Button variant="danger" type="submit" isLoading={rejectEvent.isPending}>
            Reject Event
          </Button>
        </div>
      </form>
    </Modal>
  )
}
