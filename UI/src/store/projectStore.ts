import { create } from 'zustand'
import { persist } from 'zustand/middleware'

interface ProjectStore {
  selectedProjectId: string | null
  setProject: (id: string) => void
  clearProject: () => void
}

export const useProjectStore = create<ProjectStore>()(
  persist(
    (set) => ({
      selectedProjectId: null,
      setProject: (id) => set({ selectedProjectId: id }),
      clearProject: () => set({ selectedProjectId: null }),
    }),
    {
      name: 'project-storage',
    }
  )
)
