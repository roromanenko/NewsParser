# EventDetailDto


## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**id** | **string** |  | [optional] [default to undefined]
**title** | **string** |  | [optional] [default to undefined]
**summary** | **string** |  | [optional] [default to undefined]
**status** | **string** |  | [optional] [default to undefined]
**firstSeenAt** | **string** |  | [optional] [default to undefined]
**lastUpdatedAt** | **string** |  | [optional] [default to undefined]
**articles** | [**Array&lt;EventArticleDto&gt;**](EventArticleDto.md) |  | [optional] [default to undefined]
**updates** | [**Array&lt;EventUpdateDto&gt;**](EventUpdateDto.md) |  | [optional] [default to undefined]
**contradictions** | [**Array&lt;ContradictionDto&gt;**](ContradictionDto.md) |  | [optional] [default to undefined]
**reclassifiedCount** | **number** |  | [optional] [default to undefined]
**importanceTier** | **string** |  | [optional] [default to undefined]
**importanceBaseScore** | **number** |  | [optional] [default to undefined]
**distinctSourceCount** | **number** |  | [optional] [default to undefined]

## Example

```typescript
import { EventDetailDto } from './api';

const instance: EventDetailDto = {
    id,
    title,
    summary,
    status,
    firstSeenAt,
    lastUpdatedAt,
    articles,
    updates,
    contradictions,
    reclassifiedCount,
    importanceTier,
    importanceBaseScore,
    distinctSourceCount,
};
```

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)
