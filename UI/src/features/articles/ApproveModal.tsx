import { useState } from 'react'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Spinner } from '@/components/ui/Spinner'
import { useActivePublishTargets } from '@/features/publishTargets/usePublishTargets'
import { useArticleActions } from './useArticleActions'
import { useNavigate } from 'react-router-dom'

interface ApproveModalProps {
  isOpen: boolean
  onClose: () => void
  articleId: string
}

export function ApproveModal({ isOpen, onClose, articleId }: ApproveModalProps) {
  const [selectedIds, setSelectedIds] = useState<string[]>([])
  const { approveMutation } = useArticleActions(articleId)
  const { data: targets = [], isLoading } = useActivePublishTargets()
  const navigate = useNavigate()

  function toggleTarget(id: string) {
    setSelectedIds(prev =>
      prev.includes(id) ? prev.filter(t => t !== id) : [...prev, id]
    )
  }

  async function handleApprove() {
    await approveMutation.mutateAsync(selectedIds)
    onClose()
    navigate('/articles')
  }

  return (
    <Modal isOpen={isOpen} onClose={onClose} title="Approve Article" maxWidth="sm">
      <div className="px-6 py-4">
        <p className="text-sm text-gray-600 mb-4">Select publish targets for this article:</p>
        {isLoading ? (
          <div className="flex justify-center py-6">
            <Spinner size="md" />
          </div>
        ) : targets.length === 0 ? (
          <p className="text-sm text-amber-600 py-4 text-center">
            No active publish targets available. Please configure at least one target before approving.
          </p>
        ) : (
          <div className="space-y-3">
            {targets.map(target => (
              <label key={target.id} className="flex items-center gap-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={selectedIds.includes(target.id ?? '')}
                  onChange={() => toggleTarget(target.id ?? '')}
                  className="w-4 h-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                />
                <span className="text-sm font-medium text-gray-700 flex-1">{target.name}</span>
                <Badge variant={target.platform === 'Telegram' ? 'info' : 'neutral'}>
                  {target.platform}
                </Badge>
              </label>
            ))}
          </div>
        )}
        {targets.length > 0 && selectedIds.length === 0 && (
          <p className="mt-3 text-xs text-amber-600">Select at least one target</p>
        )}
      </div>
      <div className="flex justify-end gap-3 border-t border-gray-200 px-6 py-4">
        <Button variant="secondary" onClick={onClose} disabled={approveMutation.isPending}>
          Cancel
        </Button>
        <Button
          onClick={handleApprove}
          disabled={selectedIds.length === 0 || targets.length === 0 || isLoading}
          isLoading={approveMutation.isPending}
        >
          Approve & Publish
        </Button>
      </div>
    </Modal>
  )
}
