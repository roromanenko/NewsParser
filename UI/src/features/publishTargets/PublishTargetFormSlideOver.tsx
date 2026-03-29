import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import type { PublishTargetDto } from '@/api/generated'
import { SlideOver } from '@/components/shared/SlideOver'
import { Input } from '@/components/ui/Input'
import { Textarea } from '@/components/ui/Textarea'
import { Button } from '@/components/ui/Button'
import { Toggle } from '@/components/ui/Toggle'
import { usePublishTargetMutations } from './usePublishTargetMutations'

interface Props {
  isOpen: boolean
  onClose: () => void
  target?: PublishTargetDto
}

const createSchema = z.object({
  name: z.string().min(1, 'Name is required'),
  platform: z.enum(['Telegram', 'Website'], { message: 'Select a platform' }),
  identifier: z.string().min(1, 'Identifier is required'),
  systemPrompt: z.string().min(1, 'System prompt is required'),
})

const editSchema = z.object({
  name: z.string().min(1, 'Name is required'),
  identifier: z.string().min(1, 'Identifier is required'),
  systemPrompt: z.string().min(1, 'System prompt is required'),
  isActive: z.boolean(),
})

type CreateData = z.infer<typeof createSchema>
type EditData = z.infer<typeof editSchema>

export function PublishTargetFormSlideOver({ isOpen, onClose, target }: Props) {
  const { createTarget, updateTarget } = usePublishTargetMutations()
  const isEdit = !!target

  const createForm = useForm<CreateData>({ resolver: zodResolver(createSchema) })
  const editForm = useForm<EditData>({
    resolver: zodResolver(editSchema),
    defaultValues: {
      name: target?.name ?? '',
      identifier: target?.identifier ?? '',
      systemPrompt: target?.systemPrompt ?? '',
      isActive: target?.isActive ?? true,
    },
  })

  useEffect(() => {
    if (target) {
      editForm.reset({
        name: target.name ?? '',
        identifier: target.identifier ?? '',
        systemPrompt: target.systemPrompt ?? '',
        isActive: target.isActive ?? true,
      })
    } else {
      createForm.reset()
    }
  }, [target, isOpen])

  const onCreateSubmit = async (data: CreateData) => {
    await createTarget.mutateAsync(data)
    onClose()
  }

  const onEditSubmit = async (data: EditData) => {
    if (!target?.id) return
    await updateTarget.mutateAsync({ id: target.id, data })
    onClose()
  }

  const isPending = createTarget.isPending || updateTarget.isPending

  return (
    <SlideOver isOpen={isOpen} onClose={onClose} title={isEdit ? 'Edit Publish Target' : 'Add Publish Target'}>
      {isEdit ? (
        <form onSubmit={editForm.handleSubmit(onEditSubmit)} className="flex flex-col h-full">
          <div className="flex-1 px-6 py-5 space-y-4">
            <Input label="Name" error={editForm.formState.errors.name?.message} {...editForm.register('name')} />
            <Input label="Identifier" error={editForm.formState.errors.identifier?.message} {...editForm.register('identifier')} />
            <Textarea
              label="System Prompt"
              rows={6}
              error={editForm.formState.errors.systemPrompt?.message}
              {...editForm.register('systemPrompt')}
            />
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
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Platform</label>
              <select
                className="block w-full rounded-md border border-gray-300 px-3 py-2 text-sm text-gray-900 focus:outline-none focus:ring-1 focus:border-indigo-500 focus:ring-indigo-500"
                {...createForm.register('platform')}
              >
                <option value="">Select platform...</option>
                <option value="Telegram">Telegram</option>
                <option value="Website">Website</option>
              </select>
              {createForm.formState.errors.platform && (
                <p className="mt-1 text-xs text-red-600">{createForm.formState.errors.platform.message}</p>
              )}
            </div>
            <Input label="Identifier (e.g. @channel)" error={createForm.formState.errors.identifier?.message} {...createForm.register('identifier')} />
            <Textarea
              label="System Prompt"
              rows={6}
              error={createForm.formState.errors.systemPrompt?.message}
              {...createForm.register('systemPrompt')}
            />
          </div>
          <div className="border-t border-gray-200 px-6 py-4 flex justify-end gap-3">
            <Button variant="secondary" type="button" onClick={onClose} disabled={isPending}>Cancel</Button>
            <Button type="submit" isLoading={isPending}>Add Target</Button>
          </div>
        </form>
      )}
    </SlideOver>
  )
}
