import { useState } from 'react'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { useActivePublishTargets } from '@/features/publishTargets/usePublishTargets'
import { usePublicationMutations } from './usePublicationMutations'

interface GenerateContentModalProps {
  isOpen: boolean
  onClose: () => void
  eventId: string
}

export function GenerateContentModal({ isOpen, onClose, eventId }: GenerateContentModalProps) {
  const [selectedTargetId, setSelectedTargetId] = useState('')
  const { data: targets, isLoading } = useActivePublishTargets()
  const { generateContent } = usePublicationMutations()

  const handleConfirm = () => {
    if (!selectedTargetId) return

    generateContent.mutate(
      { eventId, publishTargetId: selectedTargetId },
      { onSuccess: () => { setSelectedTargetId(''); onClose() } }
    )
  }

  return (
    <Modal isOpen={isOpen} onClose={onClose} title="Generate Content">
      <div className="p-6 space-y-4">
        <p className="text-sm text-gray-600">
          Select a publish target to generate content for this event.
        </p>

        {isLoading ? (
          <p className="text-sm text-gray-500">Loading targets...</p>
        ) : (
          <select
            className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            value={selectedTargetId}
            onChange={e => setSelectedTargetId(e.target.value)}
          >
            <option value="">Select a publish target</option>
            {targets?.map(target => (
              <option key={target.id} value={target.id}>
                {target.name} ({target.platform})
              </option>
            ))}
          </select>
        )}

        <div className="flex justify-end gap-3 pt-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button
            onClick={handleConfirm}
            disabled={!selectedTargetId || generateContent.isPending}
          >
            {generateContent.isPending ? 'Generating...' : 'Generate'}
          </Button>
        </div>
      </div>
    </Modal>
  )
}
