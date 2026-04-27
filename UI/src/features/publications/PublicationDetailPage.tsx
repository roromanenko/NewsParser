import { useEffect, useRef, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { ArrowLeft } from 'lucide-react'
import { usePublicationDetail } from './usePublicationDetail'
import { usePublicationMutations } from './usePublicationMutations'
import { PublicationEditor } from './PublicationEditor'
import { ConfirmDialog } from '@/components/shared/ConfirmDialog'
import type { PublicationDetailDto } from './types'

const MAX_UPLOAD_BYTES = 20 * 1024 * 1024
const MAX_FILES_PER_PUBLICATION = 10

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

const REGEN_MAX = 2000

export function PublicationDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { publication, isLoading } = usePublicationDetail(id!)
  const { updateContent, approve, reject, regenerate, uploadMedia, deleteMedia } = usePublicationMutations(id)

  const fileInputRef = useRef<HTMLInputElement>(null)
  const [editedContent, setEditedContent] = useState('')
  const [selectedMediaIds, setSelectedMediaIds] = useState<string[]>([])
  const [rejectReason, setRejectReason] = useState('')
  const [showRejectInput, setShowRejectInput] = useState(false)
  const [showRegenerateInput, setShowRegenerateInput] = useState(false)
  const [regenerateFeedback, setRegenerateFeedback] = useState('')
  const [deletingMediaId, setDeletingMediaId] = useState<string | null>(null)
  const [mediaToDeleteId, setMediaToDeleteId] = useState<string | null>(null)

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
  const isFailed = publication.status === 'Failed'
  const canEdit = isContentReady
  const canApprove = isContentReady
  const canReject = isContentReady || isApproved
  const canRegenerate = isContentReady || isFailed

  const accentColor = statusAccentColor(publication.status ?? '')
  const displayTitle = publication.targetName

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

  const handleRegenerate = () => {
    const trimmed = regenerateFeedback.trim()
    if (!trimmed) return
    regenerate.mutate(trimmed, {
      onSuccess: () => {
        setShowRegenerateInput(false)
        setRegenerateFeedback('')
      },
    })
  }

  const handleFileSelected = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return
    e.target.value = ''

    if (file.size > MAX_UPLOAD_BYTES) {
      alert(`File exceeds the 20 MB limit (${(file.size / 1024 / 1024).toFixed(1)} MB)`)
      return
    }

    const customCount = (publication?.availableMedia ?? []).filter(m => m.ownerKind === 'Publication').length ?? 0
    if (customCount >= MAX_FILES_PER_PUBLICATION) {
      alert(`Maximum of ${MAX_FILES_PER_PUBLICATION} custom media files reached`)
      return
    }

    uploadMedia.mutate(file)
  }

  const handleDeleteMedia = (mediaId: string) => {
    setMediaToDeleteId(mediaId)
  }

  const handleConfirmDeleteMedia = () => {
    if (!mediaToDeleteId) return
    setDeletingMediaId(mediaToDeleteId)
    deleteMedia.mutate(mediaToDeleteId, {
      onSettled: () => {
        setDeletingMediaId(null)
        setMediaToDeleteId(null)
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
              {(publication.status ?? '').toUpperCase()}
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
                  onClick={() => {
                    setShowRegenerateInput(false)
                    setShowRejectInput(v => !v)
                  }}
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
              {canRegenerate && (
                <button
                  onClick={() => {
                    setShowRejectInput(false)
                    setShowRegenerateInput(v => !v)
                  }}
                  disabled={regenerate.isPending}
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
                  REGENERATE
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
                {(publication.status ?? '').toUpperCase()}
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

      {/* Editor feedback display */}
      {publication.editorFeedback && (
        <div
          className="relative border p-5"
          style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
        >
          <div className="absolute left-0 top-0 bottom-0 w-1" style={{ backgroundColor: 'var(--caramel)' }} />
          <p className="font-caps text-[10px] tracking-widest mb-2" style={{ color: '#6b7280' }}>
            EDITOR FEEDBACK
          </p>
          <p className="font-mono text-sm whitespace-pre-wrap" style={{ color: '#9ca3af' }}>
            {publication.editorFeedback}
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

      {/* Regenerate input */}
      {showRegenerateInput && (
        <div
          className="relative border p-5 space-y-3"
          style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
        >
          <div className="absolute left-0 top-0 bottom-0 w-1" style={{ backgroundColor: 'var(--caramel)' }} />
          <p className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>
            REGENERATION FEEDBACK
          </p>
          <textarea
            value={regenerateFeedback}
            onChange={e => setRegenerateFeedback(e.target.value.slice(0, REGEN_MAX))}
            rows={4}
            className="w-full font-mono text-sm resize-y p-3 focus:outline-none"
            style={{
              background: 'rgba(61,15,15,0.4)',
              border: '1px solid rgba(255,255,255,0.1)',
              color: '#E8E8E8',
            }}
            placeholder="Describe what to change: 'make it shorter', 'emphasize the second source'…"
          />
          <div className="flex items-center justify-between">
            <span className="font-mono text-[10px]" style={{ color: '#6b7280' }}>
              {regenerateFeedback.length} / {REGEN_MAX}
            </span>
            <div className="flex gap-2">
              <button
                onClick={handleRegenerate}
                disabled={!regenerateFeedback.trim() || regenerate.isPending}
                className="px-4 py-2 font-caps text-xs tracking-wider border transition-colors disabled:opacity-40"
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
                {regenerate.isPending ? 'REQUESTING…' : 'REQUEST REGENERATION'}
              </button>
              <button
                onClick={() => setShowRegenerateInput(false)}
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
          status={publication.status ?? ''}
          editedContent={editedContent}
          onContentChange={setEditedContent}
          onSave={handleSaveContent}
          isSavePending={updateContent.isPending}
          canEdit={canEdit}
        />
      </div>

      {/* Media selection */}
      {((publication.availableMedia ?? []).length > 0 || canEdit) && (
        <div
          className="border"
          style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
        >
          <div
            className="p-5 border-b flex items-center justify-between"
            style={{ borderColor: 'rgba(255,255,255,0.1)' }}
          >
            <p className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>MEDIA</p>
            {canEdit && (
              <>
                <input
                  ref={fileInputRef}
                  type="file"
                  accept="image/*,video/mp4"
                  className="hidden"
                  onChange={handleFileSelected}
                />
                <button
                  onClick={() => fileInputRef.current?.click()}
                  disabled={!canEdit || uploadMedia.isPending}
                  className="px-3 py-1 font-caps text-[10px] tracking-wider border transition-colors disabled:opacity-50"
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
                  {uploadMedia.isPending ? 'UPLOADING…' : 'UPLOAD CUSTOM MEDIA'}
                </button>
              </>
            )}
          </div>
          <div className="p-5 grid grid-cols-3 gap-3">
            {(publication.availableMedia ?? []).map(media => {
              const isSelected = selectedMediaIds.includes(media.id!)
              const isCustom = media.ownerKind === 'Publication'
              const isDeleting = deletingMediaId === media.id
              return (
                <div
                  key={media.id}
                  onClick={() => canEdit && !isDeleting && handleToggleMedia(media.id!)}
                  className="relative overflow-hidden"
                  style={{
                    cursor: canEdit && !isDeleting ? 'pointer' : 'default',
                    border: isSelected ? '2px solid var(--caramel)' : '1px solid rgba(255,255,255,0.1)',
                  }}
                >
                  {media.kind === 'Image' ? (
                    <img src={media.url ?? ''} alt="Media file" className="w-full h-32 object-cover" />
                  ) : (
                    <div
                      className="w-full h-32 flex items-center justify-center"
                      style={{ background: 'var(--near-black)' }}
                    >
                      <span className="font-mono text-xs" style={{ color: '#6b7280' }}>Video</span>
                    </div>
                  )}
                  {isCustom && (
                    <div
                      className="absolute top-1 left-1 px-1 py-0.5 font-caps text-[9px] tracking-widest"
                      style={{ background: 'rgba(0,0,0,0.7)', color: 'var(--caramel)' }}
                    >
                      CUSTOM
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
                  {isCustom && canEdit && (
                    <button
                      onClick={e => {
                        e.stopPropagation()
                        handleDeleteMedia(media.id!)
                      }}
                      disabled={isDeleting}
                      className="absolute top-1 right-1 w-5 h-5 flex items-center justify-center font-mono text-xs transition-colors disabled:opacity-50"
                      style={{ background: 'rgba(0,0,0,0.7)', color: '#9ca3af' }}
                      onMouseEnter={e => (e.currentTarget.style.color = 'var(--crimson)')}
                      onMouseLeave={e => (e.currentTarget.style.color = '#9ca3af')}
                      title="Delete custom media"
                    >
                      ×
                    </button>
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

      <ConfirmDialog
        isOpen={mediaToDeleteId !== null}
        onClose={() => setMediaToDeleteId(null)}
        onConfirm={handleConfirmDeleteMedia}
        title="Delete Media File"
        message="Are you sure you want to delete this custom media file? This action cannot be undone."
        confirmLabel="Delete"
        variant="danger"
        isLoading={deleteMedia.isPending}
      />
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
