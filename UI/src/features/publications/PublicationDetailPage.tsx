import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Textarea'
import { Spinner } from '@/components/ui/Spinner'
import { usePublicationDetail } from './usePublicationDetail'
import { usePublicationMutations } from './usePublicationMutations'

export function PublicationDetailPage() {
  const { id } = useParams<{ id: string }>()
  const { publication, isLoading, error } = usePublicationDetail(id!)
  const { updateContent, approve, reject, send } = usePublicationMutations(id)

  const [editedContent, setEditedContent] = useState('')
  const [selectedMediaIds, setSelectedMediaIds] = useState<string[]>([])
  const [rejectReason, setRejectReason] = useState('')
  const [showRejectInput, setShowRejectInput] = useState(false)

  if (isLoading) {
    return (
      <div className="flex justify-center items-center h-64">
        <Spinner />
      </div>
    )
  }

  if (error || !publication) {
    return (
      <div className="p-6 text-red-600">
        Failed to load publication. Please try again.
      </div>
    )
  }

  const content = editedContent || publication.generatedContent
  const isContentReady = publication.status === 'ContentReady'
  const isApproved = publication.status === 'Approved'
  const canEdit = isContentReady
  const canApprove = isContentReady
  const canReject = isContentReady || isApproved
  const canSend = isContentReady || isApproved

  const handleSaveContent = () => {
    updateContent.mutate({ content, selectedMediaFileIds: selectedMediaIds })
  }

  const handleToggleMedia = (mediaId: string) => {
    setSelectedMediaIds(prev =>
      prev.includes(mediaId)
        ? prev.filter(id => id !== mediaId)
        : [...prev, mediaId]
    )
  }

  const handleReject = () => {
    if (!rejectReason.trim()) return
    reject.mutate(rejectReason, {
      onSuccess: () => { setShowRejectInput(false); setRejectReason('') }
    })
  }

  return (
    <div className="max-w-4xl mx-auto p-6 space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-gray-900">Publication Review</h1>
          <p className="text-sm text-gray-500 mt-1">
            {publication.targetName} · {publication.platform} · Status: {publication.status}
          </p>
        </div>
        <div className="flex gap-2">
          {canSend && (
            <Button onClick={() => send.mutate()} isLoading={send.isPending}>
              Send
            </Button>
          )}
          {canApprove && (
            <Button
              variant="secondary"
              onClick={() => approve.mutate()}
              isLoading={approve.isPending}
            >
              Approve
            </Button>
          )}
          {canReject && (
            <Button
              variant="danger"
              onClick={() => setShowRejectInput(v => !v)}
            >
              Reject
            </Button>
          )}
        </div>
      </div>

      {publication.rejectionReason && (
        <div className="bg-red-50 border border-red-200 rounded-md p-4">
          <p className="text-sm font-medium text-red-800">Rejection reason:</p>
          <p className="text-sm text-red-700 mt-1">{publication.rejectionReason}</p>
        </div>
      )}

      {showRejectInput && (
        <div className="bg-orange-50 border border-orange-200 rounded-md p-4 space-y-3">
          <Textarea
            label="Rejection reason"
            rows={3}
            value={rejectReason}
            onChange={e => setRejectReason(e.target.value)}
            placeholder="Explain why this content is being rejected..."
          />
          <div className="flex gap-2">
            <Button
              variant="danger"
              size="sm"
              onClick={handleReject}
              isLoading={reject.isPending}
              disabled={!rejectReason.trim()}
            >
              Confirm Rejection
            </Button>
            <Button variant="secondary" size="sm" onClick={() => setShowRejectInput(false)}>
              Cancel
            </Button>
          </div>
        </div>
      )}

      <section className="space-y-2">
        <h2 className="text-base font-medium text-gray-900">Generated Content</h2>
        <Textarea
          rows={12}
          value={content}
          onChange={e => setEditedContent(e.target.value)}
          disabled={!canEdit}
          placeholder="No content generated yet."
        />
        {canEdit && (
          <Button
            variant="secondary"
            size="sm"
            onClick={handleSaveContent}
            isLoading={updateContent.isPending}
          >
            Save Changes
          </Button>
        )}
      </section>

      {publication.availableMedia.length > 0 && (
        <section className="space-y-2">
          <h2 className="text-base font-medium text-gray-900">
            Available Media ({publication.availableMedia.length})
          </h2>
          <div className="grid grid-cols-3 gap-3">
            {publication.availableMedia.map(media => {
              const isSelected = selectedMediaIds.includes(media.id) ||
                publication.selectedMediaFileIds.includes(media.id)
              return (
                <div
                  key={media.id}
                  className={`relative cursor-pointer rounded-md border-2 overflow-hidden transition-colors ${
                    isSelected ? 'border-indigo-500' : 'border-gray-200'
                  }`}
                  onClick={() => canEdit && handleToggleMedia(media.id)}
                >
                  {media.kind === 'Image' ? (
                    <img
                      src={media.url}
                      alt="Media file"
                      className="w-full h-32 object-cover"
                    />
                  ) : (
                    <div className="w-full h-32 flex items-center justify-center bg-gray-100">
                      <span className="text-sm text-gray-500">Video</span>
                    </div>
                  )}
                  {isSelected && (
                    <div className="absolute inset-0 bg-indigo-500/20 flex items-center justify-center">
                      <span className="bg-indigo-600 text-white text-xs px-2 py-1 rounded">
                        Selected
                      </span>
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        </section>
      )}

      <section className="text-xs text-gray-400 space-y-1 pt-4 border-t border-gray-100">
        <p>Created: {new Date(publication.createdAt).toLocaleString()}</p>
        {publication.approvedAt && (
          <p>Approved: {new Date(publication.approvedAt).toLocaleString()}</p>
        )}
        {publication.publishedAt && (
          <p>Published: {new Date(publication.publishedAt).toLocaleString()}</p>
        )}
      </section>
    </div>
  )
}
