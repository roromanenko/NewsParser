import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { useProjects } from './useProjects'
import { useProjectStore } from '@/store/projectStore'

export function ProjectSwitcher() {
  const { data: projects } = useProjects()
  const { selectedProjectId, setProject } = useProjectStore()
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  useEffect(() => {
    if (!projects || projects.length === 0) return
    const isValid = projects.some(p => p.id === selectedProjectId)
    if (!selectedProjectId || !isValid) {
      setProject(projects[0].id ?? '')
    }
  }, [projects, selectedProjectId, setProject])

  function handleChange(e: React.ChangeEvent<HTMLSelectElement>) {
    const previousId = selectedProjectId
    const newId = e.target.value
    setProject(newId)
    if (previousId) {
      queryClient.invalidateQueries({ queryKey: ['project', previousId] })
    }
    const newProject = projects?.find(p => p.id === newId)
    if (newProject?.slug) {
      navigate(`/projects/${newProject.slug}/articles`)
    }
  }

  if (!projects || projects.length === 0) return null

  return (
    <div>
      <div className="font-caps text-xs tracking-wider" style={{ color: 'var(--caramel)' }}>
        PROJECT
      </div>
      <select
        value={selectedProjectId ?? ''}
        onChange={handleChange}
        className="font-mono text-sm bg-transparent border-none outline-none cursor-pointer text-gray-400"
        style={{ color: 'var(--caramel)' }}
      >
        {projects.map(p => (
          <option key={p.id} value={p.id} style={{ backgroundColor: '#1a0a0a', color: 'white' }}>
            {p.name}
          </option>
        ))}
      </select>
    </div>
  )
}
