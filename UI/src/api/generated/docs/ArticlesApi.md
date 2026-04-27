# ArticlesApi

All URIs are relative to *http://localhost*

|Method | HTTP request | Description|
|------------- | ------------- | -------------|
|[**projectsProjectIdArticlesGet**](#projectsprojectidarticlesget) | **GET** /projects/{projectId}/articles | |
|[**projectsProjectIdArticlesIdGet**](#projectsprojectidarticlesidget) | **GET** /projects/{projectId}/articles/{id} | |

# **projectsProjectIdArticlesGet**
> ArticleListItemDtoPagedResult projectsProjectIdArticlesGet()


### Example

```typescript
import {
    ArticlesApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new ArticlesApi(configuration);

let projectId: string; // (default to undefined)
let page: number; // (optional) (default to 1)
let pageSize: number; // (optional) (default to 20)
let search: string; // (optional) (default to undefined)
let sortBy: string; // (optional) (default to undefined)

const { status, data } = await apiInstance.projectsProjectIdArticlesGet(
    projectId,
    page,
    pageSize,
    search,
    sortBy
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


### Return type

**ArticleListItemDtoPagedResult**

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

# **projectsProjectIdArticlesIdGet**
> ArticleDetailDto projectsProjectIdArticlesIdGet()


### Example

```typescript
import {
    ArticlesApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new ArticlesApi(configuration);

let id: string; // (default to undefined)
let projectId: string; // (default to undefined)

const { status, data } = await apiInstance.projectsProjectIdArticlesIdGet(
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

**ArticleDetailDto**

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

