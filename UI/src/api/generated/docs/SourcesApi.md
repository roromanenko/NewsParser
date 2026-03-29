# SourcesApi

All URIs are relative to *http://localhost*

|Method | HTTP request | Description|
|------------- | ------------- | -------------|
|[**sourcesGet**](#sourcesget) | **GET** /sources | |
|[**sourcesIdDelete**](#sourcesiddelete) | **DELETE** /sources/{id} | |
|[**sourcesIdGet**](#sourcesidget) | **GET** /sources/{id} | |
|[**sourcesIdPut**](#sourcesidput) | **PUT** /sources/{id} | |
|[**sourcesPost**](#sourcespost) | **POST** /sources | |

# **sourcesGet**
> Array<SourceDto> sourcesGet()


### Example

```typescript
import {
    SourcesApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new SourcesApi(configuration);

const { status, data } = await apiInstance.sourcesGet();
```

### Parameters
This endpoint does not have any parameters.


### Return type

**Array<SourceDto>**

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

# **sourcesIdDelete**
> sourcesIdDelete()


### Example

```typescript
import {
    SourcesApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new SourcesApi(configuration);

let id: string; // (default to undefined)

const { status, data } = await apiInstance.sourcesIdDelete(
    id
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **id** | [**string**] |  | defaults to undefined|


### Return type

void (empty response body)

### Authorization

[Bearer](../README.md#Bearer)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: Not defined


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
|**200** | OK |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **sourcesIdGet**
> SourceDto sourcesIdGet()


### Example

```typescript
import {
    SourcesApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new SourcesApi(configuration);

let id: string; // (default to undefined)

const { status, data } = await apiInstance.sourcesIdGet(
    id
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **id** | [**string**] |  | defaults to undefined|


### Return type

**SourceDto**

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

# **sourcesIdPut**
> SourceDto sourcesIdPut()


### Example

```typescript
import {
    SourcesApi,
    Configuration,
    UpdateSourceRequest
} from './api';

const configuration = new Configuration();
const apiInstance = new SourcesApi(configuration);

let id: string; // (default to undefined)
let updateSourceRequest: UpdateSourceRequest; // (optional)

const { status, data } = await apiInstance.sourcesIdPut(
    id,
    updateSourceRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **updateSourceRequest** | **UpdateSourceRequest**|  | |
| **id** | [**string**] |  | defaults to undefined|


### Return type

**SourceDto**

### Authorization

[Bearer](../README.md#Bearer)

### HTTP request headers

 - **Content-Type**: application/json, text/json, application/*+json
 - **Accept**: text/plain, application/json, text/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
|**200** | OK |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **sourcesPost**
> SourceDto sourcesPost()


### Example

```typescript
import {
    SourcesApi,
    Configuration,
    CreateSourceRequest
} from './api';

const configuration = new Configuration();
const apiInstance = new SourcesApi(configuration);

let createSourceRequest: CreateSourceRequest; // (optional)

const { status, data } = await apiInstance.sourcesPost(
    createSourceRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **createSourceRequest** | **CreateSourceRequest**|  | |


### Return type

**SourceDto**

### Authorization

[Bearer](../README.md#Bearer)

### HTTP request headers

 - **Content-Type**: application/json, text/json, application/*+json
 - **Accept**: text/plain, application/json, text/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
|**200** | OK |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

