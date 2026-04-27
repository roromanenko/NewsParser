# ProjectsApi

All URIs are relative to *http://localhost*

|Method | HTTP request | Description|
|------------- | ------------- | -------------|
|[**projectsGet**](#projectsget) | **GET** /projects | |
|[**projectsIdDelete**](#projectsiddelete) | **DELETE** /projects/{id} | |
|[**projectsIdGet**](#projectsidget) | **GET** /projects/{id} | |
|[**projectsIdPut**](#projectsidput) | **PUT** /projects/{id} | |
|[**projectsIdStatusPatch**](#projectsidstatuspatch) | **PATCH** /projects/{id}/status | |
|[**projectsPost**](#projectspost) | **POST** /projects | |

# **projectsGet**
> Array<ProjectListItemDto> projectsGet()


### Example

```typescript
import {
    ProjectsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new ProjectsApi(configuration);

const { status, data } = await apiInstance.projectsGet();
```

### Parameters
This endpoint does not have any parameters.


### Return type

**Array<ProjectListItemDto>**

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

# **projectsIdDelete**
> projectsIdDelete()


### Example

```typescript
import {
    ProjectsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new ProjectsApi(configuration);

let id: string; // (default to undefined)

const { status, data } = await apiInstance.projectsIdDelete(
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

# **projectsIdGet**
> ProjectDetailDto projectsIdGet()


### Example

```typescript
import {
    ProjectsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new ProjectsApi(configuration);

let id: string; // (default to undefined)

const { status, data } = await apiInstance.projectsIdGet(
    id
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **id** | [**string**] |  | defaults to undefined|


### Return type

**ProjectDetailDto**

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

# **projectsIdPut**
> ProjectDetailDto projectsIdPut()


### Example

```typescript
import {
    ProjectsApi,
    Configuration,
    UpdateProjectRequest
} from './api';

const configuration = new Configuration();
const apiInstance = new ProjectsApi(configuration);

let id: string; // (default to undefined)
let updateProjectRequest: UpdateProjectRequest; // (optional)

const { status, data } = await apiInstance.projectsIdPut(
    id,
    updateProjectRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **updateProjectRequest** | **UpdateProjectRequest**|  | |
| **id** | [**string**] |  | defaults to undefined|


### Return type

**ProjectDetailDto**

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

# **projectsIdStatusPatch**
> projectsIdStatusPatch()


### Example

```typescript
import {
    ProjectsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new ProjectsApi(configuration);

let id: string; // (default to undefined)
let body: boolean; // (optional)

const { status, data } = await apiInstance.projectsIdStatusPatch(
    id,
    body
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **body** | **boolean**|  | |
| **id** | [**string**] |  | defaults to undefined|


### Return type

void (empty response body)

### Authorization

[Bearer](../README.md#Bearer)

### HTTP request headers

 - **Content-Type**: application/json, text/json, application/*+json
 - **Accept**: Not defined


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
|**200** | OK |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **projectsPost**
> ProjectDetailDto projectsPost()


### Example

```typescript
import {
    ProjectsApi,
    Configuration,
    CreateProjectRequest
} from './api';

const configuration = new Configuration();
const apiInstance = new ProjectsApi(configuration);

let createProjectRequest: CreateProjectRequest; // (optional)

const { status, data } = await apiInstance.projectsPost(
    createProjectRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **createProjectRequest** | **CreateProjectRequest**|  | |


### Return type

**ProjectDetailDto**

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

