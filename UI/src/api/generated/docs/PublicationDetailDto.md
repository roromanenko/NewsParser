# PublicationDetailDto


## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**id** | **string** |  | [optional] [default to undefined]
**status** | **string** |  | [optional] [default to undefined]
**targetName** | **string** |  | [optional] [default to undefined]
**platform** | **string** |  | [optional] [default to undefined]
**generatedContent** | **string** |  | [optional] [default to undefined]
**availableMedia** | [**Array&lt;MediaFileDto&gt;**](MediaFileDto.md) |  | [optional] [default to undefined]
**selectedMediaFileIds** | **Array&lt;string&gt;** |  | [optional] [default to undefined]
**createdAt** | **string** |  | [optional] [default to undefined]
**approvedAt** | **string** |  | [optional] [default to undefined]
**publishedAt** | **string** |  | [optional] [default to undefined]
**rejectionReason** | **string** |  | [optional] [default to undefined]
**editorFeedback** | **string** |  | [optional] [default to undefined]

## Example

```typescript
import { PublicationDetailDto } from './api';

const instance: PublicationDetailDto = {
    id,
    status,
    targetName,
    platform,
    generatedContent,
    availableMedia,
    selectedMediaFileIds,
    createdAt,
    approvedAt,
    publishedAt,
    rejectionReason,
    editorFeedback,
};
```

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)
