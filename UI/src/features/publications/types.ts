export interface PublicationListItemDto {
  id: string
  status: string
  targetName: string
  platform: string
  createdAt: string
  publishedAt: string | null
  eventId: string | null
  eventTitle: string | null
}

export interface MediaFileDto {
  id: string
  articleId: string
  url: string
  kind: string
  contentType: string
  sizeBytes: number
}

export interface PublicationDetailDto {
  id: string
  status: string
  targetName: string
  platform: string
  eventTitle: string | null
  generatedContent: string
  availableMedia: MediaFileDto[]
  selectedMediaFileIds: string[]
  createdAt: string
  approvedAt: string | null
  publishedAt: string | null
  rejectionReason: string | null
  editorFeedback: string | null
}
