import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Textarea'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useArticleActions } from './useArticleActions'
import { useNavigate } from 'react-router-dom'

interface RejectModalProps {
  isOpen: boolean
  onClose: () => void
  articleId: string
}

const schema = z.object({
  reason: z.string().min(1, 'Rejection reason is required').max(500, 'Max 500 characters'),
})
type FormData = z.infer<typeof schema>

export function RejectModal({ isOpen, onClose, articleId }: RejectModalProps) {
  const { rejectMutation } = useArticleActions(articleId)
  const navigate = useNavigate()

  const { register, handleSubmit, reset, formState: { errors } } = useForm<FormData>({
    resolver: zodResolver(schema),
  })

  async function onSubmit(data: FormData) {
    await rejectMutation.mutateAsync(data.reason)
    reset()
    onClose()
    navigate('/articles')
  }

  return (
    <Modal isOpen={isOpen} onClose={onClose} title="Reject Article" maxWidth="sm">
      <form onSubmit={handleSubmit(onSubmit)}>
        <div className="px-6 py-4">
          <Textarea
            label="Rejection reason"
            placeholder="Explain why this article is being rejected..."
            error={errors.reason?.message}
            rows={4}
            {...register('reason')}
          />
        </div>
        <div className="flex justify-end gap-3 border-t border-gray-200 px-6 py-4">
          <Button variant="secondary" onClick={onClose} type="button" disabled={rejectMutation.isPending}>
            Cancel
          </Button>
          <Button variant="danger" type="submit" isLoading={rejectMutation.isPending}>
            Reject Article
          </Button>
        </div>
      </form>
    </Modal>
  )
}
