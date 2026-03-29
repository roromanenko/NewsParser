import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import type { SourceDto } from '@/api/generated'
import { SlideOver } from '@/components/shared/SlideOver'
import { Input } from '@/components/ui/Input'
import { Button } from '@/components/ui/Button'
import { Toggle } from '@/components/ui/Toggle'
import { useSourceMutations } from './useSourceMutations'

interface SourceFormSlideOverProps {
  isOpen: boolean
  onClose: () => void
  source?: SourceDto
}

const createSchema = z.object({
  name: z.string().min(1, 'Name is required'),
  url: z.string().min(1, 'URL is required').url('Must be a valid URL'),
  type: z.enum(['Rss', 'Telegram'], { message: 'Select a source type' }),
})
const editSchema = z.object({
  name: z.string().min(1, 'Name is required'),
  url: z.string().min(1, 'URL is required').url('Must be a valid URL'),
  isActive: z.boolean(),
})
type CreateData = z.infer<typeof createSchema>
type EditData = z.infer<typeof editSchema>

export function SourceFormSlideOver({ isOpen, onClose, source }: SourceFormSlideOverProps) {
  const { createSource, updateSource } = useSourceMutations()
  const isEdit = !!source

  const createForm = useForm<CreateData>({ resolver: zodResolver(createSchema) })
  const editForm = useForm<EditData>({
    resolver: zodResolver(editSchema),
    defaultValues: { name: source?.name ?? '', url: source?.url ?? '', isActive: source?.isActive ?? true },
  })

  useEffect(() => {
    if (source) {
      editForm.reset({ name: source.name ?? '', url: source.url ?? '', isActive: source.isActive ?? true })
    } else {
      createForm.reset()
    }
  }, [source, isOpen])

  const onCreateSubmit = async (data: CreateData) => {
    await createSource.mutateAsync(data)
    onClose()
  }
  const onEditSubmit = async (data: EditData) => {
    if (!source?.id) return
    await updateSource.mutateAsync({ id: source.id, data })
    onClose()
  }

  const isPending = createSource.isPending || updateSource.isPending

  return (
    <SlideOver isOpen={isOpen} onClose={onClose} title={isEdit ? 'Edit Source' : 'Add Source'}>
      {isEdit ? (
        <form onSubmit={editForm.handleSubmit(onEditSubmit)} className="flex flex-col h-full">
          <div className="flex-1 px-6 py-5 space-y-4">
            <Input label="Name" error={editForm.formState.errors.name?.message} {...editForm.register('name')} />
            <Input label="URL" type="url" error={editForm.formState.errors.url?.message} {...editForm.register('url')} />
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">Status</label>
              <Toggle
                checked={editForm.watch('isActive')}
                onChange={v => editForm.setValue('isActive', v)}
                label="Active"
              />
            </div>
          </div>
          <div className="border-t border-gray-200 px-6 py-4 flex justify-end gap-3">
            <Button variant="secondary" type="button" onClick={onClose} disabled={isPending}>Cancel</Button>
            <Button type="submit" isLoading={isPending}>Save Changes</Button>
          </div>
        </form>
      ) : (
        <form onSubmit={createForm.handleSubmit(onCreateSubmit)} className="flex flex-col h-full">
          <div className="flex-1 px-6 py-5 space-y-4">
            <Input label="Name" error={createForm.formState.errors.name?.message} {...createForm.register('name')} />
            <Input label="URL" type="url" error={createForm.formState.errors.url?.message} {...createForm.register('url')} />
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Type</label>
              <select
                className="block w-full rounded-md border border-gray-300 px-3 py-2 text-sm text-gray-900 focus:outline-none focus:ring-1 focus:border-indigo-500 focus:ring-indigo-500"
                {...createForm.register('type')}
              >
                <option value="">Select type...</option>
                <option value="Rss">RSS</option>
                <option value="Telegram">Telegram</option>
              </select>
              {createForm.formState.errors.type && (
                <p className="mt-1 text-xs text-red-600">{createForm.formState.errors.type.message}</p>
              )}
            </div>
          </div>
          <div className="border-t border-gray-200 px-6 py-4 flex justify-end gap-3">
            <Button variant="secondary" type="button" onClick={onClose} disabled={isPending}>Cancel</Button>
            <Button type="submit" isLoading={isPending}>Add Source</Button>
          </div>
        </form>
      )}
    </SlideOver>
  )
}
