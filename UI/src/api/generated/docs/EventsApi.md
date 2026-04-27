# EventsApi

All URIs are relative to *http://localhost*

|Method | HTTP request | Description|
|------------- | ------------- | -------------|
|[**projectsProjectIdEventsGet**](#projectsprojectideventsget) | **GET** /projects/{projectId}/events | |
|[**projectsProjectIdEventsIdGet**](#projectsprojectideventsidget) | **GET** /projects/{projectId}/events/{id} | |
|[**projectsProjectIdEventsIdReclassifyPost**](#projectsprojectideventsidreclassifypost) | **POST** /projects/{projectId}/events/{id}/reclassify | |
|[**projectsProjectIdEventsIdResolveContradictionPost**](#projectsprojectideventsidresolvecontradictionpost) | **POST** /projects/{projectId}/events/{id}/resolve-contradiction | |
|[**projectsProjectIdEventsIdStatusPatch**](#projectsprojectideventsidstatuspatch) | **PATCH** /projects/{projectId}/events/{id}/status | |
|[**projectsProjectIdEventsMergePost**](#projectsprojectideventsmergepost) | **POST** /projects/{projectId}/events/merge | |

# **projectsProjectIdEventsGet**
> EventListItemDtoPagedResult projectsProjectIdEventsGet()


### Example

```typescript
import {
    EventsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new EventsApi(configuration);

let projectId: string; // (default to undefined)
let page: number; // (optional) (default to 1)
let pageSize: number; // (optional) (default to 20)
let search: string; // (optional) (default to undefined)
let sortBy: string; // (optional) (default to undefined)
let tier: string; // (optional) (default to undefined)

const { status, data } = await apiInstance.projectsProjectIdEventsGet(
    projectId,
    page,
    pageSize,
    search,
    sortBy,
    tier
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **projectId** | [**string**] |  | defaults to undefined|
| **page** | [**number**] |  | (optional) defaults to 1|
| **pageSize** | [**number**] |  | (optional) defaults to 20|
| **search** | [**string**] |  | (optional) defaults to undefined|
| **sortBy** | [**string**] |  | (optional) defaults to undefined|
| **tier** | [**string**] |  | (optional) defaults to undefined|


### Return type

**EventListItemDtoPagedResult**

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

# **projectsProjectIdEventsIdGet**
> EventDetailDto projectsProjectIdEventsIdGet()


### Example

```typescript
import {
    EventsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new EventsApi(configuration);

let id: string; // (default to undefined)
let projectId: string; // (default to undefined)

const { status, data } = await apiInstance.projectsProjectIdEventsIdGet(
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

**EventDetailDto**

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

# **projectsProjectIdEventsIdReclassifyPost**
> projectsProjectIdEventsIdReclassifyPost()


### Example

```typescript
import {
    EventsApi,
    Configuration,
    ReclassifyArticleRequest
} from './api';

const configuration = new Configuration();
const apiInstance = new EventsApi(configuration);

let id: string; // (default to undefined)
let projectId: string; // (default to undefined)
let reclassifyArticleRequest: ReclassifyArticleRequest; // (optional)

const { status, data } = await apiInstance.projectsProjectIdEventsIdReclassifyPost(
    id,
    projectId,
    reclassifyArticleRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **reclassifyArticleRequest** | **ReclassifyArticleRequest**|  | |
| **id** | [**string**] |  | defaults to undefined|
| **projectId** | [**string**] |  | defaults to undefined|


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

# **projectsProjectIdEventsIdResolveContradictionPost**
> projectsProjectIdEventsIdResolveContradictionPost()


### Example

```typescript
import {
    EventsApi,
    Configuration,
    ResolveContradictionRequest
} from './api';

const configuration = new Configuration();
const apiInstance = new EventsApi(configuration);

let id: string; // (default to undefined)
let projectId: string; // (default to undefined)
let resolveContradictionRequest: ResolveContradictionRequest; // (optional)

const { status, data } = await apiInstance.projectsProjectIdEventsIdResolveContradictionPost(
    id,
    projectId,
    resolveContradictionRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **resolveContradictionRequest** | **ResolveContradictionRequest**|  | |
| **id** | [**string**] |  | defaults to undefined|
| **projectId** | [**string**] |  | defaults to undefined|


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

# **projectsProjectIdEventsIdStatusPatch**
> projectsProjectIdEventsIdStatusPatch()


### Example

```typescript
import {
    EventsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new EventsApi(configuration);

let id: string; // (default to undefined)
let projectId: string; // (default to undefined)
let body: string; // (optional)

const { status, data } = await apiInstance.projectsProjectIdEventsIdStatusPatch(
    id,
    projectId,
    body
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **body** | **string**|  | |
| **id** | [**string**] |  | defaults to undefined|
| **projectId** | [**string**] |  | defaults to undefined|


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

# **projectsProjectIdEventsMergePost**
> projectsProjectIdEventsMergePost()


### Example

```typescript
import {
    EventsApi,
    Configuration,
    MergeEventsRequest
} from './api';

const configuration = new Configuration();
const apiInstance = new EventsApi(configuration);

let projectId: string; // (default to undefined)
let mergeEventsRequest: MergeEventsRequest; // (optional)

const { status, data } = await apiInstance.projectsProjectIdEventsMergePost(
    projectId,
    mergeEventsRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **mergeEventsRequest** | **MergeEventsRequest**|  | |
| **projectId** | [**string**] |  | defaults to undefined|


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

