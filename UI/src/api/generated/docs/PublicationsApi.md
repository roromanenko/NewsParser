# PublicationsApi

All URIs are relative to *http://localhost*

|Method | HTTP request | Description|
|------------- | ------------- | -------------|
|[**publicationsByEventEventIdGet**](#publicationsbyeventeventidget) | **GET** /publications/by-event/{eventId} | |
|[**publicationsGeneratePost**](#publicationsgeneratepost) | **POST** /publications/generate | |
|[**publicationsIdApprovePost**](#publicationsidapprovepost) | **POST** /publications/{id}/approve | |
|[**publicationsIdContentPut**](#publicationsidcontentput) | **PUT** /publications/{id}/content | |
|[**publicationsIdGet**](#publicationsidget) | **GET** /publications/{id} | |
|[**publicationsIdRejectPost**](#publicationsidrejectpost) | **POST** /publications/{id}/reject | |
|[**publicationsIdSendPost**](#publicationsidsendpost) | **POST** /publications/{id}/send | |

# **publicationsByEventEventIdGet**
> Array<PublicationListItemDto> publicationsByEventEventIdGet()


### Example

```typescript
import {
    PublicationsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublicationsApi(configuration);

let eventId: string; // (default to undefined)

const { status, data } = await apiInstance.publicationsByEventEventIdGet(
    eventId
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **eventId** | [**string**] |  | defaults to undefined|


### Return type

**Array<PublicationListItemDto>**

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

# **publicationsGeneratePost**
> PublicationListItemDto publicationsGeneratePost()


### Example

```typescript
import {
    PublicationsApi,
    Configuration,
    CreatePublicationRequest
} from './api';

const configuration = new Configuration();
const apiInstance = new PublicationsApi(configuration);

let createPublicationRequest: CreatePublicationRequest; // (optional)

const { status, data } = await apiInstance.publicationsGeneratePost(
    createPublicationRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **createPublicationRequest** | **CreatePublicationRequest**|  | |


### Return type

**PublicationListItemDto**

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

# **publicationsIdApprovePost**
> PublicationDetailDto publicationsIdApprovePost()


### Example

```typescript
import {
    PublicationsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublicationsApi(configuration);

let id: string; // (default to undefined)

const { status, data } = await apiInstance.publicationsIdApprovePost(
    id
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **id** | [**string**] |  | defaults to undefined|


### Return type

**PublicationDetailDto**

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

# **publicationsIdContentPut**
> PublicationDetailDto publicationsIdContentPut()


### Example

```typescript
import {
    PublicationsApi,
    Configuration,
    UpdatePublicationContentRequest
} from './api';

const configuration = new Configuration();
const apiInstance = new PublicationsApi(configuration);

let id: string; // (default to undefined)
let updatePublicationContentRequest: UpdatePublicationContentRequest; // (optional)

const { status, data } = await apiInstance.publicationsIdContentPut(
    id,
    updatePublicationContentRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **updatePublicationContentRequest** | **UpdatePublicationContentRequest**|  | |
| **id** | [**string**] |  | defaults to undefined|


### Return type

**PublicationDetailDto**

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

# **publicationsIdGet**
> PublicationDetailDto publicationsIdGet()


### Example

```typescript
import {
    PublicationsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublicationsApi(configuration);

let id: string; // (default to undefined)

const { status, data } = await apiInstance.publicationsIdGet(
    id
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **id** | [**string**] |  | defaults to undefined|


### Return type

**PublicationDetailDto**

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

# **publicationsIdRejectPost**
> PublicationDetailDto publicationsIdRejectPost()


### Example

```typescript
import {
    PublicationsApi,
    Configuration,
    RejectPublicationRequest
} from './api';

const configuration = new Configuration();
const apiInstance = new PublicationsApi(configuration);

let id: string; // (default to undefined)
let rejectPublicationRequest: RejectPublicationRequest; // (optional)

const { status, data } = await apiInstance.publicationsIdRejectPost(
    id,
    rejectPublicationRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **rejectPublicationRequest** | **RejectPublicationRequest**|  | |
| **id** | [**string**] |  | defaults to undefined|


### Return type

**PublicationDetailDto**

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

# **publicationsIdSendPost**
> PublicationDetailDto publicationsIdSendPost()


### Example

```typescript
import {
    PublicationsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublicationsApi(configuration);

let id: string; // (default to undefined)

const { status, data } = await apiInstance.publicationsIdSendPost(
    id
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **id** | [**string**] |  | defaults to undefined|


### Return type

**PublicationDetailDto**

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

