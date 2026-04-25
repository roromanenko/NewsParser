# AiRequestLogDtoPagedResult


## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**items** | [**Array&lt;AiRequestLogDto&gt;**](AiRequestLogDto.md) |  | [optional] [default to undefined]
**page** | **number** |  | [optional] [default to undefined]
**pageSize** | **number** |  | [optional] [default to undefined]
**totalCount** | **number** |  | [optional] [default to undefined]
**totalPages** | **number** |  | [optional] [readonly] [default to undefined]
**hasNextPage** | **boolean** |  | [optional] [readonly] [default to undefined]
**hasPreviousPage** | **boolean** |  | [optional] [readonly] [default to undefined]

## Example

```typescript
import { AiRequestLogDtoPagedResult } from './api';

const instance: AiRequestLogDtoPagedResult = {
    items,
    page,
    pageSize,
    totalCount,
    totalPages,
    hasNextPage,
    hasPreviousPage,
};
```

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)
