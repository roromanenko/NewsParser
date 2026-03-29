import { useState } from 'react'
import { Pencil, Trash2, Plus } from 'lucide-react'
import { usePublishTargets } from './usePublishTargets'
import { usePublishTargetMutations } from './usePublishTargetMutations'
import { PublishTargetFormSlideOver } from './PublishTargetFormSlideOver'
import { PageHeader } from '@/components/shared/PageHeader'
import { DataTable, type ColumnDef } from '@/components/shared/DataTable'
import { ConfirmDialog } from '@/components/shared/ConfirmDialog'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Toggle } from '@/components/ui/Toggle'
import type { PublishTargetDto } from '@/api/generated'

export function PublishTargetsPage() {
  const { data: targets = [], isLoading } = usePublishTargets()
  const { updateTarget, deleteTarget } = usePublishTargetMutations()
  const [slideOpen, setSlideOpen] = useState(false)
  const [editingTarget, setEditingTarget] = useState<PublishTargetDto | undefined>()
  const [deletingId, setDeletingId] = useState<string | null>(null)

  const openAdd = () => { setEditingTarget(undefined); setSlideOpen(true) }
  const openEdit = (target: PublishTargetDto) => { setEditingTarget(target); setSlideOpen(true) }
  const closeSlide = () => { setSlideOpen(false); setEditingTarget(undefined) }

  const handleToggleActive = (target: PublishTargetDto) => {
    if (!target.id) return
    updateTarget.mutate({
      id: target.id,
      data: {
        name: target.name ?? '',
        identifier: target.identifier ?? '',
        systemPrompt: target.systemPrompt ?? '',
        isActive: !target.isActive,
      },
    })
  }

  const handleDelete = async () => {
    if (!deletingId) return
    await deleteTarget.mutateAsync(deletingId)
    setDeletingId(null)
  }

  const columns: ColumnDef<PublishTargetDto>[] = [
    {
      key: 'name',
      header: 'Name',
      render: row => <span className="font-medium text-gray-900">{row.name}</span>,
    },
    {
      key: 'platform',
      header: 'Platform',
      render: row => (
        <Badge variant={row.platform === 'Telegram' ? 'info' : 'neutral'}>{row.platform ?? '—'}</Badge>
      ),
    },
    {
      key: 'identifier',
      header: 'Identifier',
      render: row => <span className="text-sm text-gray-600">{row.identifier}</span>,
    },
    {
      key: 'isActive',
      header: 'Active',
      render: row => (
        <Toggle
          checked={row.isActive ?? false}
          onChange={() => handleToggleActive(row)}
          disabled={updateTarget.isPending}
        />
      ),
    },
    {
      key: 'actions',
      header: '',
      render: row => (
        <div className="flex items-center gap-2" onClick={e => e.stopPropagation()}>
          <button
            onClick={() => openEdit(row)}
            className="p-1.5 text-gray-400 hover:text-indigo-600 hover:bg-indigo-50 rounded transition-colors"
            title="Edit"
          >
            <Pencil className="w-4 h-4" />
          </button>
          <button
            onClick={() => row.id && setDeletingId(row.id)}
            className="p-1.5 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded transition-colors"
            title="Delete"
          >
            <Trash2 className="w-4 h-4" />
          </button>
        </div>
      ),
    },
  ]

  return (
    <div>
      <PageHeader
        title="Publish Targets"
        description={isLoading ? undefined : `${targets.length} target${targets.length !== 1 ? 's' : ''}`}
        action={
          <Button leftIcon={<Plus className="w-4 h-4" />} onClick={openAdd}>
            Add Target
          </Button>
        }
      />
      <DataTable<PublishTargetDto>
        columns={columns}
        data={targets}
        isLoading={isLoading}
        emptyMessage="No publish targets configured. Add your first target."
        keyExtractor={row => row.id ?? ''}
      />
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
