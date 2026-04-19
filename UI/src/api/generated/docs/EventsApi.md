# EventsApi

All URIs are relative to *http://localhost*

|Method | HTTP request | Description|
|------------- | ------------- | -------------|
|[**eventsGet**](#eventsget) | **GET** /events | |
|[**eventsIdGet**](#eventsidget) | **GET** /events/{id} | |
|[**eventsIdReclassifyPost**](#eventsidreclassifypost) | **POST** /events/{id}/reclassify | |
|[**eventsIdResolveContradictionPost**](#eventsidresolvecontradictionpost) | **POST** /events/{id}/resolve-contradiction | |
|[**eventsIdStatusPatch**](#eventsidstatuspatch) | **PATCH** /events/{id}/status | |
|[**eventsMergePost**](#eventsmergepost) | **POST** /events/merge | |

# **eventsGet**
> EventListItemDtoPagedResult eventsGet()


### Example

```typescript
import {
    EventsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new EventsApi(configuration);

let page: number; // (optional) (default to 1)
let pageSize: number; // (optional) (default to 20)
let search: string; // (optional) (default to undefined)
let sortBy: string; // (optional) (default to undefined)
let tier: string; // (optional) (default to undefined)

const { status, data } = await apiInstance.eventsGet(
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

# **eventsIdGet**
> EventDetailDto eventsIdGet()


### Example

```typescript
import {
    EventsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new EventsApi(configuration);

let id: string; // (default to undefined)

const { status, data } = await apiInstance.eventsIdGet(
    id
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **id** | [**string**] |  | defaults to undefined|


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

# **eventsIdReclassifyPost**
> eventsIdReclassifyPost()


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
let reclassifyArticleRequest: ReclassifyArticleRequest; // (optional)

const { status, data } = await apiInstance.eventsIdReclassifyPost(
    id,
    reclassifyArticleRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **reclassifyArticleRequest** | **ReclassifyArticleRequest**|  | |
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

# **eventsIdResolveContradictionPost**
> eventsIdResolveContradictionPost()


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
let resolveContradictionRequest: ResolveContradictionRequest; // (optional)

const { status, data } = await apiInstance.eventsIdResolveContradictionPost(
    id,
    resolveContradictionRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **resolveContradictionRequest** | **ResolveContradictionRequest**|  | |
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

# **eventsIdStatusPatch**
> eventsIdStatusPatch()


### Example

```typescript
import {
    EventsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new EventsApi(configuration);

let id: string; // (default to undefined)
let body: string; // (optional)

const { status, data } = await apiInstance.eventsIdStatusPatch(
    id,
    body
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **body** | **string**|  | |
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

# **eventsMergePost**
> eventsMergePost()


### Example

```typescript
import {
    EventsApi,
    Configuration,
    MergeEventsRequest
} from './api';

const configuration = new Configuration();
const apiInstance = new EventsApi(configuration);

let mergeEventsRequest: MergeEventsRequest; // (optional)

const { status, data } = await apiInstance.eventsMergePost(
    mergeEventsRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **mergeEventsRequest** | **MergeEventsRequest**|  | |


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

