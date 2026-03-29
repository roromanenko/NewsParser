import { useState } from 'react'
import { Plus, Pencil, Trash2 } from 'lucide-react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useUsers } from './useUsers'
import { PageHeader } from '@/components/shared/PageHeader'
import { DataTable, type ColumnDef } from '@/components/shared/DataTable'
import { Modal } from '@/components/ui/Modal'
import { SlideOver } from '@/components/shared/SlideOver'
import { ConfirmDialog } from '@/components/shared/ConfirmDialog'
import { Input } from '@/components/ui/Input'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import type { UserDto } from '@/api/generated'

const createSchema = z.object({
  firstName: z.string().min(1, 'First name is required'),
  lastName: z.string().min(1, 'Last name is required'),
  email: z.string().min(1, 'Email is required').email('Invalid email'),
  password: z.string().min(8, 'Password must be at least 8 characters'),
  role: z.enum(['Editor', 'Admin']),
})
type CreateFormData = z.infer<typeof createSchema>

const editSchema = z.object({
  firstName: z.string().min(1, 'First name is required'),
  lastName: z.string().min(1, 'Last name is required'),
  email: z.string().min(1, 'Email is required').email('Invalid email'),
})
type EditFormData = z.infer<typeof editSchema>

export function UsersPage() {
  const [modalOpen, setModalOpen] = useState(false)
  const [editTarget, setEditTarget] = useState<UserDto | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<UserDto | null>(null)

  const { createEditor, updateEditor, deleteEditor, usersQuery } = useUsers({
    onCreated: () => { setModalOpen(false); createReset() },
    onUpdated: () => { setEditTarget(null); editReset() },
    onDeleted: () => setDeleteTarget(null),
  })

  const {
    register: createRegister,
    handleSubmit: createHandleSubmit,
    reset: createReset,
    formState: { errors: createErrors },
  } = useForm<CreateFormData>({ resolver: zodResolver(createSchema), defaultValues: { role: 'Editor' } })

  const {
    register: editRegister,
    handleSubmit: editHandleSubmit,
    reset: editReset,
    formState: { errors: editErrors },
  } = useForm<EditFormData>({ resolver: zodResolver(editSchema) })

  const openEdit = (user: UserDto) => {
    setEditTarget(user)
    editReset({ firstName: user.firstName ?? '', lastName: user.lastName ?? '', email: user.email ?? '' })
  }

  const onCreateSubmit = (data: CreateFormData) => createEditor.mutate(data)
  const onEditSubmit = (data: EditFormData) => {
    if (!editTarget?.id) return
    updateEditor.mutate({ id: editTarget.id, ...data })
  }

  const columns: ColumnDef<UserDto>[] = [
    { key: 'firstName', header: 'First Name', render: row => <span className="font-medium text-gray-900">{row.firstName}</span> },
    { key: 'lastName', header: 'Last Name', render: row => <span>{row.lastName}</span> },
    { key: 'email', header: 'Email', render: row => <span className="text-gray-600">{row.email}</span> },
    { key: 'role', header: 'Role', render: row => <Badge variant="info">{row.role ?? 'Editor'}</Badge> },
    {
      key: 'actions',
      header: 'Actions',
      className: 'text-right',
      render: row => row.role === 'Admin' ? null : (
        <div className="flex items-center gap-2 justify-end">
          <button
            onClick={() => openEdit(row)}
            className="text-gray-400 hover:text-blue-600 transition-colors"
            title="Edit"
          >
            <Pencil className="w-4 h-4" />
          </button>
          <button
            onClick={() => setDeleteTarget(row)}
            className="text-gray-400 hover:text-red-600 transition-colors"
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
        title="Users"
        description="Manage user accounts"
        action={
          <Button leftIcon={<Plus className="w-4 h-4" />} onClick={() => setModalOpen(true)}>
            Add User
          </Button>
        }
      />
      <DataTable<UserDto>
        columns={columns}
        data={usersQuery.data ?? []}
        emptyMessage="No users added yet. Add your first user to get started."
        keyExtractor={row => row.id ?? row.email ?? ''}
      />

      <Modal isOpen={modalOpen} onClose={() => { setModalOpen(false); createReset() }} title="Add User" maxWidth="sm">
        <form onSubmit={createHandleSubmit(onCreateSubmit)}>
          <div className="px-6 py-4 space-y-4">
            <div className="grid grid-cols-2 gap-3">
              <Input label="First Name" error={createErrors.firstName?.message} {...createRegister('firstName')} />
              <Input label="Last Name" error={createErrors.lastName?.message} {...createRegister('lastName')} />
            </div>
            <Input label="Email" type="email" error={createErrors.email?.message} {...createRegister('email')} />
            <Input label="Password" type="password" error={createErrors.password?.message} {...createRegister('password')} />
            <div className="flex flex-col gap-1">
              <label className="text-sm font-medium text-gray-700">Role</label>
              <select
                className="block w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                {...createRegister('role')}
              >
                <option value="Editor">Editor</option>
                <option value="Admin">Admin</option>
              </select>
              {createErrors.role && <p className="text-xs text-red-500">{createErrors.role.message}</p>}
            </div>
          </div>
          <div className="flex justify-end gap-3 border-t border-gray-200 px-6 py-4">
            <Button variant="secondary" type="button" onClick={() => { setModalOpen(false); createReset() }} disabled={createEditor.isPending}>
              Cancel
            </Button>
            <Button type="submit" isLoading={createEditor.isPending}>
              Create User
            </Button>
          </div>
        </form>
      </Modal>

      <SlideOver isOpen={editTarget !== null} onClose={() => { setEditTarget(null); editReset() }} title="Edit User">
        <form onSubmit={editHandleSubmit(onEditSubmit)}>
          <div className="px-6 py-4 space-y-4">
            <div className="grid grid-cols-2 gap-3">
              <Input label="First Name" error={editErrors.firstName?.message} {...editRegister('firstName')} />
              <Input label="Last Name" error={editErrors.lastName?.message} {...editRegister('lastName')} />
            </div>
            <Input label="Email" type="email" error={editErrors.email?.message} {...editRegister('email')} />
          </div>
          <div className="flex justify-end gap-3 border-t border-gray-200 px-6 py-4">
            <Button variant="secondary" type="button" onClick={() => { setEditTarget(null); editReset() }} disabled={updateEditor.isPending}>
              Cancel
            </Button>
            <Button type="submit" isLoading={updateEditor.isPending}>
              Save Changes
            </Button>
          </div>
        </form>
      </SlideOver>

      <ConfirmDialog
        isOpen={deleteTarget !== null}
        onClose={() => setDeleteTarget(null)}
        onConfirm={() => { if (deleteTarget?.id) deleteEditor.mutate(deleteTarget.id) }}
        title="Delete User"
        message={`Are you sure you want to delete ${deleteTarget?.firstName} ${deleteTarget?.lastName}? This action cannot be undone.`}
        confirmLabel="Delete"
        variant="danger"
        isLoading={deleteEditor.isPending}
      />
    </div>
  )
}
