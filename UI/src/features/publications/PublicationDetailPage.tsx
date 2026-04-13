import { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { ArrowLeft } from 'lucide-react'
import { usePublicationDetail } from './usePublicationDetail'
import { usePublicationMutations } from './usePublicationMutations'
import { PublicationEditor } from './PublicationEditor'
import type { PublicationDetailDto } from './types'

function statusAccentColor(status: string): string {
  if (status === 'ContentReady') return 'var(--caramel)'
  if (status === 'Approved' || status === 'Published') return '#22c55e'
  if (status === 'Rejected' || status === 'Failed') return 'var(--crimson)'
  return '#6b7280'
}

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

function buildEditorContent(publication: PublicationDetailDto): string {
  return publication.generatedContent ?? ''
}

function buildInitialMediaIds(publication: PublicationDetailDto): string[] {
  return publication.selectedMediaFileIds ?? []
}

export function PublicationDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { publication, isLoading } = usePublicationDetail(id!)
  const { updateContent, approve, reject } = usePublicationMutations(id)

  const [editedContent, setEditedContent] = useState('')
  const [selectedMediaIds, setSelectedMediaIds] = useState<string[]>([])
  const [rejectReason, setRejectReason] = useState('')
  const [showRejectInput, setShowRejectInput] = useState(false)

  useEffect(() => {
    if (!publication) return
    setEditedContent(buildEditorContent(publication))
    setSelectedMediaIds(buildInitialMediaIds(publication))
  }, [publication])

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="font-mono text-sm animate-pulse" style={{ color: '#9ca3af' }}>
          Loading…
        </div>
      </div>
    )
  }

  if (!publication) {
    return (
      <div className="font-mono text-sm text-center py-16" style={{ color: '#9ca3af' }}>
        Publication not found.
      </div>
    )
  }

  const isContentReady = publication.status === 'ContentReady'
  const isApproved = publication.status === 'Approved'
  const canEdit = isContentReady
  const canApprove = isContentReady
  const canReject = isContentReady || isApproved

  const accentColor = statusAccentColor(publication.status)
  const displayTitle = publication.eventTitle ?? publication.targetName

  const handleSaveContent = () => {
    updateContent.mutate({ content: editedContent, selectedMediaFileIds: selectedMediaIds })
  }

  const handleToggleMedia = (mediaId: string) => {
    setSelectedMediaIds(prev =>
      prev.includes(mediaId) ? prev.filter(mid => mid !== mediaId) : [...prev, mediaId]
    )
  }

  const handleApprove = () => {
    updateContent.mutate(
      { content: editedContent, selectedMediaFileIds: selectedMediaIds },
      {
        onSuccess: () => approve.mutate(),
        onError: (error) => console.error('Failed to save content before approving:', error),
      }
    )
  }

  const handleReject = () => {
    if (!rejectReason.trim()) return
    reject.mutate(rejectReason, {
      onSuccess: () => {
        setShowRejectInput(false)
        setRejectReason('')
      },
    })
  }

  return (
    <div className="max-w-4xl space-y-6">
      {/* Back button */}
      <button
        onClick={() => navigate(-1)}
        className="flex items-center gap-2 font-mono text-xs transition-colors"
        style={{ color: '#6b7280' }}
        onMouseEnter={e => (e.currentTarget.style.color = 'var(--caramel)')}
        onMouseLeave={e => (e.currentTarget.style.color = '#6b7280')}
      >
        <ArrowLeft className="w-4 h-4" />
        BACK TO PUBLICATIONS
      </button>

      {/* Header card */}
      <div
        className="relative border overflow-hidden"
        style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
      >
        <div className="absolute left-0 top-0 bottom-0 w-1 z-10" style={{ backgroundColor: accentColor }} />

        <div className="p-6">
          {/* Top row: status chip + action buttons */}
          <div className="flex items-start justify-between gap-4 flex-wrap mb-4">
            <span className="font-caps text-xs tracking-widest" style={{ color: accentColor }}>
              {publication.status.toUpperCase()}
            </span>

            <div className="flex items-center gap-2 flex-wrap">
              {canApprove && (
                <button
                  onClick={handleApprove}
                  disabled={approve.isPending || updateContent.isPending}
                  className="px-4 py-2 font-caps text-xs tracking-wider border transition-colors disabled:opacity-50"
                  style={{ borderColor: 'rgba(255,255,255,0.2)', color: '#9ca3af' }}
                  onMouseEnter={e => {
                    e.currentTarget.style.borderColor = 'var(--caramel)'
                    e.currentTarget.style.color = 'var(--caramel)'
                  }}
                  onMouseLeave={e => {
                    e.currentTarget.style.borderColor = 'rgba(255,255,255,0.2)'
                    e.currentTarget.style.color = '#9ca3af'
                  }}
                >
                  APPROVE
                </button>
              )}
              {canReject && (
                <button
                  onClick={() => setShowRejectInput(v => !v)}
                  disabled={reject.isPending}
                  className="px-4 py-2 font-caps text-xs tracking-wider border transition-colors disabled:opacity-50"
                  style={{ borderColor: 'rgba(255,255,255,0.2)', color: '#9ca3af' }}
                  onMouseEnter={e => {
                    e.currentTarget.style.borderColor = 'var(--rust)'
                    e.currentTarget.style.color = 'var(--rust)'
                  }}
                  onMouseLeave={e => {
                    e.currentTarget.style.borderColor = 'rgba(255,255,255,0.2)'
                    e.currentTarget.style.color = '#9ca3af'
                  }}
                >
                  REJECT
                </button>
              )}
            </div>
          </div>

          {/* Title */}
          <h1 className="font-display text-4xl mb-4" style={{ color: '#E8E8E8' }}>
            {displayTitle}
          </h1>

          {/* Stats row */}
          <div
            className="flex gap-3 pt-4 border-t flex-wrap"
            style={{ borderColor: 'rgba(255,255,255,0.1)' }}
          >
            <div className="px-3 py-1.5" style={{ background: 'var(--near-black)' }}>
              <span className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>PLATFORM </span>
              <span className="font-mono text-sm" style={{ color: '#E8E8E8' }}>{publication.platform}</span>
            </div>
            <div className="px-3 py-1.5" style={{ background: 'var(--near-black)' }}>
              <span className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>TARGET </span>
              <span className="font-mono text-sm" style={{ color: '#E8E8E8' }}>{publication.targetName}</span>
            </div>
            <div className="px-3 py-1.5" style={{ background: 'var(--near-black)' }}>
              <span className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>STATUS </span>
              <span className="font-caps text-xs tracking-widest" style={{ color: accentColor }}>
                {publication.status.toUpperCase()}
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* Rejection reason display */}
      {publication.rejectionReason && (
        <div
          className="relative border p-5"
          style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
        >
          <div className="absolute left-0 top-0 bottom-0 w-1" style={{ backgroundColor: 'var(--rust)' }} />
          <p className="font-caps text-[10px] tracking-widest mb-2" style={{ color: '#6b7280' }}>
            REJECTION REASON
          </p>
          <p className="font-mono text-sm" style={{ color: '#9ca3af' }}>
            {publication.rejectionReason}
          </p>
        </div>
      )}

      {/* Reject input */}
      {showRejectInput && (
        <div
          className="relative border p-5 space-y-3"
          style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
        >
          <div className="absolute left-0 top-0 bottom-0 w-1" style={{ backgroundColor: 'var(--rust)' }} />
          <p className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>
            REJECTION REASON
          </p>
          <textarea
            value={rejectReason}
            onChange={e => setRejectReason(e.target.value)}
            rows={3}
            className="w-full font-mono text-sm resize-y p-3 focus:outline-none"
            style={{
              background: 'rgba(61,15,15,0.4)',
              border: '1px solid rgba(255,255,255,0.1)',
              color: '#E8E8E8',
            }}
            placeholder="Explain why this content is being rejected..."
          />
          <div className="flex gap-2">
            <button
              onClick={handleReject}
              disabled={!rejectReason.trim() || reject.isPending}
              className="px-4 py-2 font-caps text-xs tracking-wider border transition-colors disabled:opacity-40"
              style={{ borderColor: 'rgba(255,255,255,0.2)', color: '#9ca3af' }}
              onMouseEnter={e => {
                e.currentTarget.style.borderColor = 'var(--rust)'
                e.currentTarget.style.color = 'var(--rust)'
              }}
              onMouseLeave={e => {
                e.currentTarget.style.borderColor = 'rgba(255,255,255,0.2)'
                e.currentTarget.style.color = '#9ca3af'
              }}
            >
              CONFIRM REJECTION
            </button>
            <button
              onClick={() => setShowRejectInput(false)}
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
          </div>
        </div>
      )}

      {/* Status-aware content area */}
      <div
        className="border"
        style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
      >
        <div className="p-5 border-b" style={{ borderColor: 'rgba(255,255,255,0.1)' }}>
          <p className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>CONTENT</p>
        </div>

        <ContentArea
          status={publication.status}
          editedContent={editedContent}
          onContentChange={setEditedContent}
          onSave={handleSaveContent}
          isSavePending={updateContent.isPending}
          canEdit={canEdit}
        />
      </div>

      {/* Media selection */}
      {publication.availableMedia.length > 0 && (
        <div
          className="border"
          style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
        >
          <div className="p-5 border-b" style={{ borderColor: 'rgba(255,255,255,0.1)' }}>
            <p className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>MEDIA</p>
          </div>
          <div className="p-5 grid grid-cols-3 gap-3">
            {publication.availableMedia.map(media => {
              const isSelected = selectedMediaIds.includes(media.id)
              return (
                <div
                  key={media.id}
                  onClick={() => canEdit && handleToggleMedia(media.id)}
                  className="relative overflow-hidden"
                  style={{
                    cursor: canEdit ? 'pointer' : 'default',
                    border: isSelected ? '2px solid var(--caramel)' : '1px solid rgba(255,255,255,0.1)',
                  }}
                >
                  {media.kind === 'Image' ? (
                    <img src={media.url} alt="Media file" className="w-full h-32 object-cover" />
                  ) : (
                    <div
                      className="w-full h-32 flex items-center justify-center"
                      style={{ background: 'var(--near-black)' }}
                    >
                      <span className="font-mono text-xs" style={{ color: '#6b7280' }}>Video</span>
                    </div>
                  )}
                  {isSelected && (
                    <div
                      className="absolute inset-0 flex items-center justify-center"
                      style={{ background: 'rgba(180,100,40,0.25)' }}
                    >
                      <span
                        className="font-caps text-[10px] tracking-widest px-2 py-1"
                        style={{ background: 'var(--caramel)', color: '#fff' }}
                      >
                        SELECTED
                      </span>
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        </div>
      )}

      {/* Metadata footer */}
      <div
        className="border"
        style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
      >
        <div className="p-5 border-b" style={{ borderColor: 'rgba(255,255,255,0.1)' }}>
          <p className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>METADATA</p>
        </div>
        <div className="p-5 space-y-2">
          <p className="font-mono text-xs" style={{ color: '#6b7280' }}>
            Created: {formatDate(publication.createdAt)}
          </p>
          {publication.approvedAt && (
            <p className="font-mono text-xs" style={{ color: '#6b7280' }}>
              Approved: {formatDate(publication.approvedAt)}
            </p>
          )}
          {publication.publishedAt && (
            <p className="font-mono text-xs" style={{ color: '#6b7280' }}>
              Published: {formatDate(publication.publishedAt)}
            </p>
          )}
        </div>
      </div>
    </div>
  )
}

interface ContentAreaProps {
  status: string
  editedContent: string
  onContentChange: (value: string) => void
  onSave: () => void
  isSavePending: boolean
  canEdit: boolean
}

function ContentArea({ status, editedContent, onContentChange, onSave, isSavePending, canEdit }: ContentAreaProps) {
  if (status === 'Created') {
    return (
      <div className="p-5">
        <p className="font-mono text-sm" style={{ color: '#6b7280' }}>
          Content generation pending. The worker will generate content shortly.
        </p>
      </div>
    )
  }

  if (status === 'GenerationInProgress') {
    return (
      <div className="p-5">
        <p className="font-mono text-sm animate-pulse" style={{ color: '#6b7280' }}>
          Generating content…
        </p>
      </div>
    )
  }

  if (status === 'ContentReady') {
    return (
      <div>
        <PublicationEditor value={editedContent} onChange={onContentChange} disabled={false} />
        <div className="px-5 pb-5 pt-3">
          <button
            onClick={onSave}
            disabled={isSavePending || !canEdit}
            className="px-4 py-2 font-caps text-xs tracking-wider border transition-colors disabled:opacity-50"
            style={{ borderColor: 'rgba(255,255,255,0.2)', color: '#9ca3af' }}
            onMouseEnter={e => {
              e.currentTarget.style.borderColor = 'var(--caramel)'
              e.currentTarget.style.color = 'var(--caramel)'
            }}
            onMouseLeave={e => {
              e.currentTarget.style.borderColor = 'rgba(255,255,255,0.2)'
              e.currentTarget.style.color = '#9ca3af'
            }}
          >
            {isSavePending ? 'SAVING…' : 'SAVE CHANGES'}
          </button>
        </div>
      </div>
    )
  }

  if (status === 'Approved') {
    return (
      <div>
        <PublicationEditor value={editedContent} onChange={onContentChange} disabled={true} />
        <div className="px-5 pb-5 pt-3">
          <p className="font-mono text-xs" style={{ color: '#6b7280' }}>
            Awaiting publication by worker.
          </p>
        </div>
      </div>
    )
  }

  return <PublicationEditor value={editedContent} onChange={onContentChange} disabled={true} />
}
