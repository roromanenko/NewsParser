# AiOperationsApi

All URIs are relative to *http://localhost*

|Method | HTTP request | Description|
|------------- | ------------- | -------------|
|[**aiOperationsMetricsGet**](#aioperationsmetricsget) | **GET** /ai-operations/metrics | |
|[**aiOperationsRequestsGet**](#aioperationsrequestsget) | **GET** /ai-operations/requests | |
|[**aiOperationsRequestsIdGet**](#aioperationsrequestsidget) | **GET** /ai-operations/requests/{id} | |

# **aiOperationsMetricsGet**
> AiOperationsMetricsDto aiOperationsMetricsGet()


### Example

```typescript
import {
    AiOperationsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new AiOperationsApi(configuration);

let from: string; // (optional) (default to undefined)
let to: string; // (optional) (default to undefined)
let provider: string; // (optional) (default to undefined)
let worker: string; // (optional) (default to undefined)
let model: string; // (optional) (default to undefined)

const { status, data } = await apiInstance.aiOperationsMetricsGet(
    from,
    to,
    provider,
    worker,
    model
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **from** | [**string**] |  | (optional) defaults to undefined|
| **to** | [**string**] |  | (optional) defaults to undefined|
| **provider** | [**string**] |  | (optional) defaults to undefined|
| **worker** | [**string**] |  | (optional) defaults to undefined|
| **model** | [**string**] |  | (optional) defaults to undefined|


### Return type

**AiOperationsMetricsDto**

### Authorization

[Bearer](../README.md#Bearer)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: text/plain, application/json, text/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
|**200** | OK |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **aiOperationsRequestsGet**
> AiRequestLogDtoPagedResult aiOperationsRequestsGet()


### Example

```typescript
import {
    AiOperationsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new AiOperationsApi(configuration);

let from: string; // (optional) (default to undefined)
let to: string; // (optional) (default to undefined)
let provider: string; // (optional) (default to undefined)
let worker: string; // (optional) (default to undefined)
let model: string; // (optional) (default to undefined)
let status: string; // (optional) (default to undefined)
let search: string; // (optional) (default to undefined)
let page: number; // (optional) (default to undefined)
let pageSize: number; // (optional) (default to undefined)

const { status, data } = await apiInstance.aiOperationsRequestsGet(
    from,
    to,
    provider,
    worker,
    model,
    status,
    search,
    page,
    pageSize
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **from** | [**string**] |  | (optional) defaults to undefined|
| **to** | [**string**] |  | (optional) defaults to undefined|
| **provider** | [**string**] |  | (optional) defaults to undefined|
| **worker** | [**string**] |  | (optional) defaults to undefined|
| **model** | [**string**] |  | (optional) defaults to undefined|
| **status** | [**string**] |  | (optional) defaults to undefined|
| **search** | [**string**] |  | (optional) defaults to undefined|
| **page** | [**number**] |  | (optional) defaults to undefined|
| **pageSize** | [**number**] |  | (optional) defaults to undefined|


### Return type

**AiRequestLogDtoPagedResult**

### Authorization

[Bearer](../README.md#Bearer)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: text/plain, application/json, text/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
|**200** | OK |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **aiOperationsRequestsIdGet**
> AiRequestLogDto aiOperationsRequestsIdGet()


### Example

```typescript
import {
    AiOperationsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new AiOperationsApi(configuration);

let id: string; // (default to undefined)

const { status, data } = await apiInstance.aiOperationsRequestsIdGet(
    id
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **id** | [**string**] |  | defaults to undefined|


### Return type

**AiRequestLogDto**

### Authorization

[Bearer](../README.md#Bearer)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: text/plain, application/json, text/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
|**200** | OK |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

