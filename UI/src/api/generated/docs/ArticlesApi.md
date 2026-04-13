# ArticlesApi

All URIs are relative to *http://localhost*

|Method | HTTP request | Description|
|------------- | ------------- | -------------|
|[**articlesGet**](#articlesget) | **GET** /articles | |
|[**articlesIdGet**](#articlesidget) | **GET** /articles/{id} | |

# **articlesGet**
> ArticleListItemDtoPagedResult articlesGet()


### Example

```typescript
import {
    ArticlesApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new ArticlesApi(configuration);

let page: number; // (optional) (default to 1)
let pageSize: number; // (optional) (default to 20)
let search: string; // (optional) (default to undefined)
let sortBy: string; // (optional) (default to undefined)

const { status, data } = await apiInstance.articlesGet(
    page,
    pageSize,
    search,
    sortBy
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
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

# **articlesIdGet**
> ArticleDetailDto articlesIdGet()


### Example

```typescript
import {
    ArticlesApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new ArticlesApi(configuration);

let id: string; // (default to undefined)

const { status, data } = await apiInstance.articlesIdGet(
    id
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **id** | [**string**] |  | defaults to undefined|


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

