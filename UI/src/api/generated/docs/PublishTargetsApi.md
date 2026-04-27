# PublishTargetsApi

All URIs are relative to *http://localhost*

|Method | HTTP request | Description|
|------------- | ------------- | -------------|
|[**projectsProjectIdPublishTargetsActiveGet**](#projectsprojectidpublishtargetsactiveget) | **GET** /projects/{projectId}/publish-targets/active | |
|[**projectsProjectIdPublishTargetsGet**](#projectsprojectidpublishtargetsget) | **GET** /projects/{projectId}/publish-targets | |
|[**projectsProjectIdPublishTargetsIdDelete**](#projectsprojectidpublishtargetsiddelete) | **DELETE** /projects/{projectId}/publish-targets/{id} | |
|[**projectsProjectIdPublishTargetsIdGet**](#projectsprojectidpublishtargetsidget) | **GET** /projects/{projectId}/publish-targets/{id} | |
|[**projectsProjectIdPublishTargetsIdPut**](#projectsprojectidpublishtargetsidput) | **PUT** /projects/{projectId}/publish-targets/{id} | |
|[**projectsProjectIdPublishTargetsPost**](#projectsprojectidpublishtargetspost) | **POST** /projects/{projectId}/publish-targets | |

# **projectsProjectIdPublishTargetsActiveGet**
> Array<PublishTargetDto> projectsProjectIdPublishTargetsActiveGet()


### Example

```typescript
import {
    PublishTargetsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublishTargetsApi(configuration);

let projectId: string; // (default to undefined)

const { status, data } = await apiInstance.projectsProjectIdPublishTargetsActiveGet(
    projectId
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **projectId** | [**string**] |  | defaults to undefined|


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

# **projectsProjectIdPublishTargetsGet**
> Array<PublishTargetDto> projectsProjectIdPublishTargetsGet()


### Example

```typescript
import {
    PublishTargetsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublishTargetsApi(configuration);

let projectId: string; // (default to undefined)

const { status, data } = await apiInstance.projectsProjectIdPublishTargetsGet(
    projectId
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **projectId** | [**string**] |  | defaults to undefined|


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

# **projectsProjectIdPublishTargetsIdDelete**
> projectsProjectIdPublishTargetsIdDelete()


### Example

```typescript
import {
    PublishTargetsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublishTargetsApi(configuration);

let id: string; // (default to undefined)
let projectId: string; // (default to undefined)

const { status, data } = await apiInstance.projectsProjectIdPublishTargetsIdDelete(
    id,
    projectId
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **id** | [**string**] |  | defaults to undefined|
| **projectId** | [**string**] |  | defaults to undefined|


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

# **projectsProjectIdPublishTargetsIdGet**
> PublishTargetDto projectsProjectIdPublishTargetsIdGet()


### Example

```typescript
import {
    PublishTargetsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublishTargetsApi(configuration);

let id: string; // (default to undefined)
let projectId: string; // (default to undefined)

const { status, data } = await apiInstance.projectsProjectIdPublishTargetsIdGet(
    id,
    projectId
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **id** | [**string**] |  | defaults to undefined|
| **projectId** | [**string**] |  | defaults to undefined|


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

# **projectsProjectIdPublishTargetsIdPut**
> PublishTargetDto projectsProjectIdPublishTargetsIdPut()


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
let projectId: string; // (default to undefined)
let updatePublishTargetRequest: UpdatePublishTargetRequest; // (optional)

const { status, data } = await apiInstance.projectsProjectIdPublishTargetsIdPut(
    id,
    projectId,
    updatePublishTargetRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **updatePublishTargetRequest** | **UpdatePublishTargetRequest**|  | |
| **id** | [**string**] |  | defaults to undefined|
| **projectId** | [**string**] |  | defaults to undefined|


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

# **projectsProjectIdPublishTargetsPost**
> PublishTargetDto projectsProjectIdPublishTargetsPost()


### Example

```typescript
import {
    PublishTargetsApi,
    Configuration,
    CreatePublishTargetRequest
} from './api';

const configuration = new Configuration();
const apiInstance = new PublishTargetsApi(configuration);

let projectId: string; // (default to undefined)
let createPublishTargetRequest: CreatePublishTargetRequest; // (optional)

const { status, data } = await apiInstance.projectsProjectIdPublishTargetsPost(
    projectId,
    createPublishTargetRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **createPublishTargetRequest** | **CreatePublishTargetRequest**|  | |
| **projectId** | [**string**] |  | defaults to undefined|


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

