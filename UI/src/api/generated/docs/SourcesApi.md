# SourcesApi

All URIs are relative to *http://localhost*

|Method | HTTP request | Description|
|------------- | ------------- | -------------|
|[**projectsProjectIdSourcesGet**](#projectsprojectidsourcesget) | **GET** /projects/{projectId}/sources | |
|[**projectsProjectIdSourcesIdDelete**](#projectsprojectidsourcesiddelete) | **DELETE** /projects/{projectId}/sources/{id} | |
|[**projectsProjectIdSourcesIdGet**](#projectsprojectidsourcesidget) | **GET** /projects/{projectId}/sources/{id} | |
|[**projectsProjectIdSourcesIdPut**](#projectsprojectidsourcesidput) | **PUT** /projects/{projectId}/sources/{id} | |
|[**projectsProjectIdSourcesPost**](#projectsprojectidsourcespost) | **POST** /projects/{projectId}/sources | |

# **projectsProjectIdSourcesGet**
> Array<SourceDto> projectsProjectIdSourcesGet()


### Example

```typescript
import {
    SourcesApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new SourcesApi(configuration);

let projectId: string; // (default to undefined)

const { status, data } = await apiInstance.projectsProjectIdSourcesGet(
    projectId
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **projectId** | [**string**] |  | defaults to undefined|


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

# **projectsProjectIdSourcesIdDelete**
> projectsProjectIdSourcesIdDelete()


### Example

```typescript
import {
    SourcesApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new SourcesApi(configuration);

let id: string; // (default to undefined)
let projectId: string; // (default to undefined)

const { status, data } = await apiInstance.projectsProjectIdSourcesIdDelete(
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

# **projectsProjectIdSourcesIdGet**
> SourceDto projectsProjectIdSourcesIdGet()


### Example

```typescript
import {
    SourcesApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new SourcesApi(configuration);

let id: string; // (default to undefined)
let projectId: string; // (default to undefined)

const { status, data } = await apiInstance.projectsProjectIdSourcesIdGet(
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

# **projectsProjectIdSourcesIdPut**
> SourceDto projectsProjectIdSourcesIdPut()


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
let projectId: string; // (default to undefined)
let updateSourceRequest: UpdateSourceRequest; // (optional)

const { status, data } = await apiInstance.projectsProjectIdSourcesIdPut(
    id,
    projectId,
    updateSourceRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **updateSourceRequest** | **UpdateSourceRequest**|  | |
| **id** | [**string**] |  | defaults to undefined|
| **projectId** | [**string**] |  | defaults to undefined|


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

# **projectsProjectIdSourcesPost**
> SourceDto projectsProjectIdSourcesPost()


### Example

```typescript
import {
    SourcesApi,
    Configuration,
    CreateSourceRequest
} from './api';

const configuration = new Configuration();
const apiInstance = new SourcesApi(configuration);

let projectId: string; // (default to undefined)
let createSourceRequest: CreateSourceRequest; // (optional)

const { status, data } = await apiInstance.projectsProjectIdSourcesPost(
    projectId,
    createSourceRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **createSourceRequest** | **CreateSourceRequest**|  | |
| **projectId** | [**string**] |  | defaults to undefined|


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

