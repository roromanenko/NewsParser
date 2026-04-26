import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Plus, Pencil, Trash2, ToggleLeft, ToggleRight } from 'lucide-react'
import { useProjects } from './useProjects'
import {
  useCreateProject,
  useUpdateProject,
  useToggleProjectActive,
  useDeleteProject,
} from './useProjectMutations'
import { SlideOver } from '@/components/shared/SlideOver'
import { ConfirmDialog } from '@/components/shared/ConfirmDialog'
import type { ProjectListItemDto } from './useProjects'

const inputClass =
  'w-full px-4 py-3 font-mono text-sm text-gray-200 placeholder-gray-600 focus:outline-none transition-colors'
const inputStyle = {
  background: 'var(--near-black)',
  border: '1px solid rgba(255,255,255,0.1)',
}

const projectSchema = z.object({
  name: z.string().min(1, 'Name is required'),
  slug: z.string().optional(),
  analyzerPromptText: z.string().min(1, 'Analyzer prompt is required'),
  categories: z.string().min(1, 'At least one category is required'),
  outputLanguage: z.string().min(2).max(5),
  outputLanguageName: z.string().min(1, 'Output language name is required'),
})

type ProjectFormValues = z.infer<typeof projectSchema>

interface ProjectFormSlideOverProps {
  isOpen: boolean
  onClose: () => void
  project?: ProjectListItemDto & { analyzerPromptText?: string; categories?: string[]; outputLanguage?: string; outputLanguageName?: string }
}

function ProjectFormSlideOver({ isOpen, onClose, project }: ProjectFormSlideOverProps) {
  const createProject = useCreateProject()
  const updateProject = useUpdateProject()

  const { register, handleSubmit, formState: { errors }, reset } = useForm<ProjectFormValues>({
    resolver: zodResolver(projectSchema),
    defaultValues: project ? {
      name: project.name,
      slug: project.slug,
      analyzerPromptText: project.analyzerPromptText ?? '',
      categories: (project.categories ?? []).join(', '),
      outputLanguage: project.outputLanguage ?? 'uk',
      outputLanguageName: project.outputLanguageName ?? 'Ukrainian',
    } : {
      outputLanguage: 'uk',
      outputLanguageName: 'Ukrainian',
    },
  })

  async function onSubmit(values: ProjectFormValues) {
    const categories = values.categories.split(',').map(c => c.trim()).filter(Boolean)
    if (project) {
      await updateProject.mutateAsync({
        id: project.id,
        data: {
          name: values.name,
          analyzerPromptText: values.analyzerPromptText,
          categories,
          outputLanguage: values.outputLanguage,
          outputLanguageName: values.outputLanguageName,
          isActive: project.isActive,
        },
      })
    } else {
      await createProject.mutateAsync({
        name: values.name,
        slug: values.slug || undefined,
        analyzerPromptText: values.analyzerPromptText,
        categories,
        outputLanguage: values.outputLanguage,
        outputLanguageName: values.outputLanguageName,
      })
    }
    reset()
    onClose()
  }

  const title = project ? 'Edit Project' : 'New Project'

  return (
    <SlideOver isOpen={isOpen} onClose={onClose} title={title}>
      <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 p-6">
        <div>
          <label className="block text-xs font-caps tracking-wider text-gray-400 mb-1">NAME *</label>
          <input {...register('name')} className={inputClass} style={inputStyle} placeholder="My Project" />
          {errors.name && <p className="text-red-400 text-xs mt-1">{errors.name.message}</p>}
        </div>

        {!project && (
          <div>
            <label className="block text-xs font-caps tracking-wider text-gray-400 mb-1">SLUG (optional)</label>
            <input {...register('slug')} className={inputClass} style={inputStyle} placeholder="my-project (auto-derived if empty)" />
          </div>
        )}

        <div>
          <label className="block text-xs font-caps tracking-wider text-gray-400 mb-1">ANALYZER PROMPT *</label>
          <textarea
            {...register('analyzerPromptText')}
            rows={8}
            className={inputClass}
            style={inputStyle}
            placeholder="System prompt for the article analyzer..."
          />
          {errors.analyzerPromptText && <p className="text-red-400 text-xs mt-1">{errors.analyzerPromptText.message}</p>}
        </div>

        <div>
          <label className="block text-xs font-caps tracking-wider text-gray-400 mb-1">CATEGORIES (comma-separated) *</label>
          <input
            {...register('categories')}
            className={inputClass}
            style={inputStyle}
            placeholder="Politics, Economics, Technology"
          />
          {errors.categories && <p className="text-red-400 text-xs mt-1">{errors.categories.message}</p>}
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-xs font-caps tracking-wider text-gray-400 mb-1">OUTPUT LANGUAGE CODE *</label>
            <input {...register('outputLanguage')} className={inputClass} style={inputStyle} placeholder="uk" />
            {errors.outputLanguage && <p className="text-red-400 text-xs mt-1">{errors.outputLanguage.message}</p>}
          </div>
          <div>
            <label className="block text-xs font-caps tracking-wider text-gray-400 mb-1">OUTPUT LANGUAGE NAME *</label>
            <input {...register('outputLanguageName')} className={inputClass} style={inputStyle} placeholder="Ukrainian" />
            {errors.outputLanguageName && <p className="text-red-400 text-xs mt-1">{errors.outputLanguageName.message}</p>}
          </div>
        </div>

        <button
          type="submit"
          disabled={createProject.isPending || updateProject.isPending}
          className="w-full py-3 font-caps tracking-wider text-sm transition-colors"
          style={{ background: 'var(--crimson)', color: 'white' }}
        >
          {project ? 'SAVE CHANGES' : 'CREATE PROJECT'}
        </button>
      </form>
    </SlideOver>
  )
}

export function ProjectsPage() {
  const { data: projects = [], isLoading } = useProjects()
  const toggleActive = useToggleProjectActive()
  const deleteProject = useDeleteProject()

  const [slideOpen, setSlideOpen] = useState(false)
  const [editingProject, setEditingProject] = useState<ProjectListItemDto | undefined>()
  const [deletingId, setDeletingId] = useState<string | null>(null)

  const openAdd = () => { setEditingProject(undefined); setSlideOpen(true) }
  const openEdit = (project: ProjectListItemDto) => { setEditingProject(project); setSlideOpen(true) }
  const closeSlide = () => { setSlideOpen(false); setEditingProject(undefined) }

  const handleDelete = async () => {
    if (!deletingId) return
    await deleteProject.mutateAsync(deletingId)
    setDeletingId(null)
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-3xl" style={{ color: 'var(--crimson)' }}>Projects</h1>
        <button
          onClick={openAdd}
          className="flex items-center gap-2 px-4 py-2 font-caps text-xs tracking-wider transition-colors"
          style={{ background: 'var(--crimson)', color: 'white' }}
        >
          <Plus size={14} />
          NEW PROJECT
        </button>
      </div>

      {isLoading ? (
        <p className="text-gray-400 font-mono text-sm">Loading projects...</p>
      ) : projects.length === 0 ? (
        <p className="text-gray-400 font-mono text-sm">No projects yet.</p>
      ) : (
        <div className="space-y-3">
          {projects.map(project => (
            <div
              key={project.id}
              className="flex items-center justify-between p-4"
              style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(255,255,255,0.08)' }}
            >
              <div>
                <div className="font-display text-lg text-white">{project.name}</div>
                <div className="font-mono text-xs text-gray-500">{project.slug}</div>
              </div>
              <div className="flex items-center gap-3">
                <button
                  onClick={() => toggleActive.mutate({ id: project.id, isActive: !project.isActive })}
                  className="flex items-center gap-1 text-xs font-caps tracking-wider"
                  style={{ color: project.isActive ? 'var(--caramel)' : 'var(--gray-500)' }}
                >
                  {project.isActive ? <ToggleRight size={18} /> : <ToggleLeft size={18} />}
                  {project.isActive ? 'ACTIVE' : 'INACTIVE'}
                </button>
                <button onClick={() => openEdit(project)} className="p-2 text-gray-400 hover:text-white transition-colors">
                  <Pencil size={14} />
                </button>
                <button onClick={() => setDeletingId(project.id)} className="p-2 text-gray-400 hover:text-red-400 transition-colors">
                  <Trash2 size={14} />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      <ProjectFormSlideOver isOpen={slideOpen} onClose={closeSlide} project={editingProject} />

      <ConfirmDialog
        isOpen={deletingId !== null}
        title="Delete Project"
        message="Delete this project? This will fail if the project still has sources, events, or publications."
        confirmLabel="Delete"
        onConfirm={handleDelete}
        onCancel={() => setDeletingId(null)}
      />
    </div>
  )
}
