import { useState } from 'react'
import { Plus, Send, Globe, Settings, Trash2, CheckCircle, XCircle } from 'lucide-react'
import { usePublishTargets } from './usePublishTargets'
import { usePublishTargetMutations } from './usePublishTargetMutations'
import { PublishTargetFormSlideOver } from './PublishTargetFormSlideOver'
import { ConfirmDialog } from '@/components/shared/ConfirmDialog'
import type { PublishTargetDto } from '@/api/generated'

function getStatusColor(isActive?: boolean | null) {
  return isActive ? 'var(--caramel)' : '#6B7280'
}

function getPlatformIcon(platform?: string | null) {
  return platform === 'Telegram' ? Send : Globe
}

export function PublishTargetsPage() {
  const { data: targets = [], isLoading } = usePublishTargets()
  const { deleteTarget } = usePublishTargetMutations()
  const [slideOpen, setSlideOpen] = useState(false)
  const [editingTarget, setEditingTarget] = useState<PublishTargetDto | undefined>()
  const [deletingId, setDeletingId] = useState<string | null>(null)

  const openAdd = () => { setEditingTarget(undefined); setSlideOpen(true) }
  const openEdit = (target: PublishTargetDto) => { setEditingTarget(target); setSlideOpen(true) }
  const closeSlide = () => { setSlideOpen(false); setEditingTarget(undefined) }

  const handleDelete = async () => {
    if (!deletingId) return
    await deleteTarget.mutateAsync(deletingId)
    setDeletingId(null)
  }

  const activeCount = targets.filter(t => t.isActive).length
  const telegramCount = targets.filter(t => t.platform === 'Telegram').length
  const websiteCount = targets.filter(t => t.platform === 'Website').length

  return (
    <div className="p-8">
      {/* Header */}
      <div
        className="mb-8 flex items-center justify-between border-b pb-6"
        style={{ borderColor: 'rgba(255,255,255,0.1)' }}
      >
        <div>
          <h1 className="font-display text-5xl mb-2">Publish Targets</h1>
          <p className="font-mono text-sm" style={{ color: '#9ca3af' }}>
            {isLoading ? 'Loading…' : `${activeCount} of ${targets.length} targets active`}
          </p>
        </div>
        <button
          onClick={openAdd}
          className="px-6 py-3 text-white font-caps text-xs tracking-wider flex items-center gap-2 transition-opacity hover:opacity-90"
          style={{ background: 'var(--crimson)' }}
        >
          <Plus className="w-4 h-4" />
          ADD TARGET
        </button>
      </div>

      {/* Platform cards grid */}
      {isLoading ? (
        <div className="grid grid-cols-3 gap-6">
          {Array.from({ length: 3 }).map((_, i) => (
            <div
              key={i}
              className="h-64 animate-pulse"
              style={{ background: 'rgba(61,15,15,0.4)', border: '1px solid rgba(255,255,255,0.1)' }}
            />
          ))}
        </div>
      ) : targets.length === 0 ? (
        <div className="font-mono text-sm text-center py-16" style={{ color: '#9ca3af' }}>
          No publish targets configured. Add your first target.
        </div>
      ) : (
        <div className="grid grid-cols-3 gap-6">
          {targets.map(target => (
            <TargetCard
              key={target.id}
              target={target}
              onEdit={openEdit}
              onDelete={id => setDeletingId(id)}
            />
          ))}
        </div>
      )}

      {/* Analytics overview */}
      {!isLoading && targets.length > 0 && (
        <div className="mt-8 grid grid-cols-4 gap-4">
          <StatCard icon={Send} label="TOTAL TARGETS" value={targets.length} />
          <StatCard icon={CheckCircle} label="ACTIVE" value={activeCount} color="var(--caramel)" />
          <StatCard icon={XCircle} label="INACTIVE" value={targets.length - activeCount} color="#6B7280" />
          <StatCard icon={Globe} label="TELEGRAM / WEB" value={`${telegramCount} / ${websiteCount}`} />
        </div>
      )}

      <PublishTargetFormSlideOver isOpen={slideOpen} onClose={closeSlide} target={editingTarget} />
      <ConfirmDialog
        isOpen={!!deletingId}
        onClose={() => setDeletingId(null)}
        onConfirm={handleDelete}
        title="Delete Publish Target"
        message="Are you sure you want to delete this publish target? This action cannot be undone."
        confirmLabel="Delete"
        variant="danger"
        isLoading={deleteTarget.isPending}
      />
    </div>
  )
}

interface TargetCardProps {
  target: PublishTargetDto
  onEdit: (target: PublishTargetDto) => void
  onDelete: (id: string) => void
}

function TargetCard({ target, onEdit, onDelete }: TargetCardProps) {
  const statusColor = getStatusColor(target.isActive)
  const PlatformIcon = getPlatformIcon(target.platform)

  return (
    <div
      className="relative border p-6 cursor-pointer transition-colors group overflow-hidden"
      style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
      onMouseEnter={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
      onMouseLeave={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
    >
      {/* Status indicator corner */}
      <div
        className="absolute top-0 right-0 w-16 h-16 pointer-events-none"
        style={{ background: `linear-gradient(135deg, transparent 50%, ${statusColor} 50%)` }}
      />

      {/* Platform icon */}
      <div className="mb-4">
        <PlatformIcon className="w-10 h-10" style={{ color: 'var(--caramel)' }} />
      </div>

      {/* Name */}
      <h3
        className="font-display text-2xl mb-2 transition-colors"
        style={{ color: '#E8E8E8' }}
        onMouseEnter={e => (e.currentTarget.style.color = 'var(--caramel)')}
        onMouseLeave={e => (e.currentTarget.style.color = '#E8E8E8')}
      >
        {target.name}
      </h3>

      {/* Platform type badge */}
      <div className="mb-4">
        <span className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>
          {target.platform ?? '—'}
        </span>
      </div>

      {/* Identifier */}
      <div
        className="mb-4 pt-4 border-t"
        style={{ borderColor: 'rgba(255,255,255,0.1)' }}
      >
        <div className="font-caps text-xs tracking-widest mb-1" style={{ color: 'var(--caramel)' }}>
          IDENTIFIER
        </div>
        <div className="font-mono text-sm truncate" style={{ color: '#9ca3af' }}>
          {target.identifier || '—'}
        </div>
      </div>

      {/* Status */}
      <div className="flex items-center gap-2 mb-4">
        {target.isActive ? (
          <CheckCircle className="w-4 h-4" style={{ color: statusColor }} />
        ) : (
          <XCircle className="w-4 h-4" style={{ color: statusColor }} />
        )}
        <span className="font-caps text-xs tracking-wider" style={{ color: statusColor }}>
          {target.isActive ? 'ACTIVE' : 'INACTIVE'}
        </span>
      </div>

      {/* Action buttons */}
      <div className="flex gap-2">
        <button
          onClick={() => onEdit(target)}
          className="flex-1 py-2 border font-caps text-xs tracking-wider flex items-center justify-center gap-2 transition-colors"
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
          <Settings className="w-3 h-3" />
          CONFIGURE
        </button>
        <button
          onClick={() => target.id && onDelete(target.id)}
          className="px-3 py-2 border transition-colors"
          style={{ borderColor: 'rgba(255,255,255,0.2)', color: '#9ca3af' }}
          onMouseEnter={e => {
            e.currentTarget.style.borderColor = 'var(--crimson)'
            e.currentTarget.style.color = 'var(--crimson)'
          }}
          onMouseLeave={e => {
            e.currentTarget.style.borderColor = 'rgba(255,255,255,0.2)'
            e.currentTarget.style.color = '#9ca3af'
          }}
          title="Delete"
        >
          <Trash2 className="w-3 h-3" />
        </button>
      </div>
    </div>
  )
}

interface StatCardProps {
  icon: React.ElementType
  label: string
  value: number | string
  color?: string
}

function StatCard({ icon: Icon, label, value, color = 'var(--caramel)' }: StatCardProps) {
  return (
    <div
      className="border p-6"
      style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
    >
      <div className="flex items-center gap-3 mb-3">
        <Icon className="w-5 h-5" style={{ color }} />
        <div className="font-caps text-xs tracking-widest" style={{ color }}>
          {label}
        </div>
      </div>
      <div className="font-display text-4xl" style={{ color: '#E8E8E8' }}>
        {value}
      </div>
    </div>
  )
}
