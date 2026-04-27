export interface PublicationListItemDto {
  id?: string
  status?: string | null
  targetName?: string | null
  platform?: string | null
  createdAt?: string
  publishedAt?: string | null
  eventId?: string | null
  eventTitle?: string | null
}

export interface MediaFileDto {
  id?: string
  articleId?: string | null
  url?: string | null
  kind?: string | null
  contentType?: string | null
  sizeBytes?: number
  ownerKind?: string | null
}

export interface PublicationDetailDto {
  id?: string
  status?: string | null
  targetName?: string | null
  platform?: string | null
  eventTitle?: string | null
  generatedContent?: string | null
  availableMedia?: MediaFileDto[] | null
  selectedMediaFileIds?: string[] | null
  createdAt?: string
  approvedAt?: string | null
  publishedAt?: string | null
  rejectionReason?: string | null
  editorFeedback?: string | null
}
