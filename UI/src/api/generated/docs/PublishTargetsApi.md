# PublishTargetsApi

All URIs are relative to *http://localhost*

|Method | HTTP request | Description|
|------------- | ------------- | -------------|
|[**publishTargetsActiveGet**](#publishtargetsactiveget) | **GET** /publish-targets/active | |
|[**publishTargetsGet**](#publishtargetsget) | **GET** /publish-targets | |
|[**publishTargetsIdDelete**](#publishtargetsiddelete) | **DELETE** /publish-targets/{id} | |
|[**publishTargetsIdGet**](#publishtargetsidget) | **GET** /publish-targets/{id} | |
|[**publishTargetsIdPut**](#publishtargetsidput) | **PUT** /publish-targets/{id} | |
|[**publishTargetsPost**](#publishtargetspost) | **POST** /publish-targets | |

# **publishTargetsActiveGet**
> Array<PublishTargetDto> publishTargetsActiveGet()


### Example

```typescript
import {
    PublishTargetsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublishTargetsApi(configuration);

const { status, data } = await apiInstance.publishTargetsActiveGet();
```

### Parameters
This endpoint does not have any parameters.


### Return type

**Array<PublishTargetDto>**

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

# **publishTargetsGet**
> Array<PublishTargetDto> publishTargetsGet()


### Example

```typescript
import {
    PublishTargetsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublishTargetsApi(configuration);

const { status, data } = await apiInstance.publishTargetsGet();
```

### Parameters
This endpoint does not have any parameters.


### Return type

**Array<PublishTargetDto>**

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

# **publishTargetsIdDelete**
> publishTargetsIdDelete()


### Example

```typescript
import {
    PublishTargetsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublishTargetsApi(configuration);

let id: string; // (default to undefined)

const { status, data } = await apiInstance.publishTargetsIdDelete(
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

# **publishTargetsIdGet**
> PublishTargetDto publishTargetsIdGet()


### Example

```typescript
import {
    PublishTargetsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublishTargetsApi(configuration);

let id: string; // (default to undefined)

const { status, data } = await apiInstance.publishTargetsIdGet(
    id
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **id** | [**string**] |  | defaults to undefined|


### Return type

**PublishTargetDto**

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

# **publishTargetsIdPut**
> PublishTargetDto publishTargetsIdPut()


### Example

```typescript
import {
    PublishTargetsApi,
    Configuration,
    UpdatePublishTargetRequest
} from './api';

const configuration = new Configuration();
const apiInstance = new PublishTargetsApi(configuration);

let id: string; // (default to undefined)
let updatePublishTargetRequest: UpdatePublishTargetRequest; // (optional)

const { status, data } = await apiInstance.publishTargetsIdPut(
    id,
    updatePublishTargetRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **updatePublishTargetRequest** | **UpdatePublishTargetRequest**|  | |
| **id** | [**string**] |  | defaults to undefined|


### Return type

**PublishTargetDto**

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

# **publishTargetsPost**
> PublishTargetDto publishTargetsPost()


### Example

```typescript
import {
    PublishTargetsApi,
    Configuration,
    CreatePublishTargetRequest
} from './api';

const configuration = new Configuration();
const apiInstance = new PublishTargetsApi(configuration);

let createPublishTargetRequest: CreatePublishTargetRequest; // (optional)

const { status, data } = await apiInstance.publishTargetsPost(
    createPublishTargetRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **createPublishTargetRequest** | **CreatePublishTargetRequest**|  | |


### Return type

**PublishTargetDto**

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

