# AiOperationsMetricsDto


## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**totalCostUsd** | **number** |  | [optional] [default to undefined]
**totalCalls** | **number** |  | [optional] [default to undefined]
**successCalls** | **number** |  | [optional] [default to undefined]
**errorCalls** | **number** |  | [optional] [default to undefined]
**averageLatencyMs** | **number** |  | [optional] [default to undefined]
**totalInputTokens** | **number** |  | [optional] [default to undefined]
**totalOutputTokens** | **number** |  | [optional] [default to undefined]
**totalCacheCreationInputTokens** | **number** |  | [optional] [default to undefined]
**totalCacheReadInputTokens** | **number** |  | [optional] [default to undefined]
**timeSeries** | [**Array&lt;AiMetricsTimeBucketDto&gt;**](AiMetricsTimeBucketDto.md) |  | [optional] [default to undefined]
**byModel** | [**Array&lt;AiMetricsBreakdownRowDto&gt;**](AiMetricsBreakdownRowDto.md) |  | [optional] [default to undefined]
**byWorker** | [**Array&lt;AiMetricsBreakdownRowDto&gt;**](AiMetricsBreakdownRowDto.md) |  | [optional] [default to undefined]
**byProvider** | [**Array&lt;AiMetricsBreakdownRowDto&gt;**](AiMetricsBreakdownRowDto.md) |  | [optional] [default to undefined]

## Example

```typescript
import { AiOperationsMetricsDto } from './api';

const instance: AiOperationsMetricsDto = {
    totalCostUsd,
    totalCalls,
    successCalls,
    errorCalls,
    averageLatencyMs,
    totalInputTokens,
    totalOutputTokens,
    totalCacheCreationInputTokens,
    totalCacheReadInputTokens,
    timeSeries,
    byModel,
    byWorker,
    byProvider,
};
```

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)
