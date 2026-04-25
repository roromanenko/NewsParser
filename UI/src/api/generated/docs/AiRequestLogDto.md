# AiRequestLogDto


## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**id** | **string** |  | [optional] [default to undefined]
**timestamp** | **string** |  | [optional] [default to undefined]
**worker** | **string** |  | [optional] [default to undefined]
**provider** | **string** |  | [optional] [default to undefined]
**operation** | **string** |  | [optional] [default to undefined]
**model** | **string** |  | [optional] [default to undefined]
**inputTokens** | **number** |  | [optional] [default to undefined]
**outputTokens** | **number** |  | [optional] [default to undefined]
**cacheCreationInputTokens** | **number** |  | [optional] [default to undefined]
**cacheReadInputTokens** | **number** |  | [optional] [default to undefined]
**totalTokens** | **number** |  | [optional] [default to undefined]
**costUsd** | **number** |  | [optional] [default to undefined]
**latencyMs** | **number** |  | [optional] [default to undefined]
**status** | **string** |  | [optional] [default to undefined]
**errorMessage** | **string** |  | [optional] [default to undefined]
**correlationId** | **string** |  | [optional] [default to undefined]
**articleId** | **string** |  | [optional] [default to undefined]

## Example

```typescript
import { AiRequestLogDto } from './api';

const instance: AiRequestLogDto = {
    id,
    timestamp,
    worker,
    provider,
    operation,
    model,
    inputTokens,
    outputTokens,
    cacheCreationInputTokens,
    cacheReadInputTokens,
    totalTokens,
    costUsd,
    latencyMs,
    status,
    errorMessage,
    correlationId,
    articleId,
};
```

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)
