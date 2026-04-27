# PublicationsApi

All URIs are relative to *http://localhost*

|Method | HTTP request | Description|
|------------- | ------------- | -------------|
|[**projectsProjectIdPublicationsByEventEventIdGet**](#projectsprojectidpublicationsbyeventeventidget) | **GET** /projects/{projectId}/publications/by-event/{eventId} | |
|[**projectsProjectIdPublicationsGeneratePost**](#projectsprojectidpublicationsgeneratepost) | **POST** /projects/{projectId}/publications/generate | |
|[**projectsProjectIdPublicationsGet**](#projectsprojectidpublicationsget) | **GET** /projects/{projectId}/publications | |
|[**projectsProjectIdPublicationsIdApprovePost**](#projectsprojectidpublicationsidapprovepost) | **POST** /projects/{projectId}/publications/{id}/approve | |
|[**projectsProjectIdPublicationsIdContentPut**](#projectsprojectidpublicationsidcontentput) | **PUT** /projects/{projectId}/publications/{id}/content | |
|[**projectsProjectIdPublicationsIdGet**](#projectsprojectidpublicationsidget) | **GET** /projects/{projectId}/publications/{id} | |
|[**projectsProjectIdPublicationsIdMediaMediaIdDelete**](#projectsprojectidpublicationsidmediamediaiddelete) | **DELETE** /projects/{projectId}/publications/{id}/media/{mediaId} | |
|[**projectsProjectIdPublicationsIdMediaPost**](#projectsprojectidpublicationsidmediapost) | **POST** /projects/{projectId}/publications/{id}/media | |
|[**projectsProjectIdPublicationsIdRegeneratePost**](#projectsprojectidpublicationsidregeneratepost) | **POST** /projects/{projectId}/publications/{id}/regenerate | |
|[**projectsProjectIdPublicationsIdRejectPost**](#projectsprojectidpublicationsidrejectpost) | **POST** /projects/{projectId}/publications/{id}/reject | |
|[**projectsProjectIdPublicationsIdSendPost**](#projectsprojectidpublicationsidsendpost) | **POST** /projects/{projectId}/publications/{id}/send | |

# **projectsProjectIdPublicationsByEventEventIdGet**
> Array<PublicationListItemDto> projectsProjectIdPublicationsByEventEventIdGet()


### Example

```typescript
import {
    PublicationsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublicationsApi(configuration);

let eventId: string; // (default to undefined)
let projectId: string; // (default to undefined)

const { status, data } = await apiInstance.projectsProjectIdPublicationsByEventEventIdGet(
    eventId,
    projectId
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **eventId** | [**string**] |  | defaults to undefined|
| **projectId** | [**string**] |  | defaults to undefined|


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

# **projectsProjectIdPublicationsGeneratePost**
> PublicationListItemDto projectsProjectIdPublicationsGeneratePost()


### Example

```typescript
import {
    PublicationsApi,
    Configuration,
    CreatePublicationRequest
} from './api';

const configuration = new Configuration();
const apiInstance = new PublicationsApi(configuration);

let projectId: string; // (default to undefined)
let createPublicationRequest: CreatePublicationRequest; // (optional)

const { status, data } = await apiInstance.projectsProjectIdPublicationsGeneratePost(
    projectId,
    createPublicationRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **createPublicationRequest** | **CreatePublicationRequest**|  | |
| **projectId** | [**string**] |  | defaults to undefined|


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

# **projectsProjectIdPublicationsGet**
> PublicationListItemDtoPagedResult projectsProjectIdPublicationsGet()


### Example

```typescript
import {
    PublicationsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublicationsApi(configuration);

let projectId: string; // (default to undefined)
let page: number; // (optional) (default to 1)
let pageSize: number; // (optional) (default to 20)

const { status, data } = await apiInstance.projectsProjectIdPublicationsGet(
    projectId,
    page,
    pageSize
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **projectId** | [**string**] |  | defaults to undefined|
| **page** | [**number**] |  | (optional) defaults to 1|
| **pageSize** | [**number**] |  | (optional) defaults to 20|


### Return type

**PublicationListItemDtoPagedResult**

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

# **projectsProjectIdPublicationsIdApprovePost**
> PublicationDetailDto projectsProjectIdPublicationsIdApprovePost()


### Example

```typescript
import {
    PublicationsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublicationsApi(configuration);

let id: string; // (default to undefined)
let projectId: string; // (default to undefined)

const { status, data } = await apiInstance.projectsProjectIdPublicationsIdApprovePost(
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

# **projectsProjectIdPublicationsIdContentPut**
> PublicationDetailDto projectsProjectIdPublicationsIdContentPut()


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
let projectId: string; // (default to undefined)
let updatePublicationContentRequest: UpdatePublicationContentRequest; // (optional)

const { status, data } = await apiInstance.projectsProjectIdPublicationsIdContentPut(
    id,
    projectId,
    updatePublicationContentRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **updatePublicationContentRequest** | **UpdatePublicationContentRequest**|  | |
| **id** | [**string**] |  | defaults to undefined|
| **projectId** | [**string**] |  | defaults to undefined|


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

# **projectsProjectIdPublicationsIdGet**
> PublicationDetailDto projectsProjectIdPublicationsIdGet()


### Example

```typescript
import {
    PublicationsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublicationsApi(configuration);

let id: string; // (default to undefined)
let projectId: string; // (default to undefined)

const { status, data } = await apiInstance.projectsProjectIdPublicationsIdGet(
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

# **projectsProjectIdPublicationsIdMediaMediaIdDelete**
> projectsProjectIdPublicationsIdMediaMediaIdDelete()


### Example

```typescript
import {
    PublicationsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublicationsApi(configuration);

let id: string; // (default to undefined)
let mediaId: string; // (default to undefined)
let projectId: string; // (default to undefined)

const { status, data } = await apiInstance.projectsProjectIdPublicationsIdMediaMediaIdDelete(
    id,
    mediaId,
    projectId
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **id** | [**string**] |  | defaults to undefined|
| **mediaId** | [**string**] |  | defaults to undefined|
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

# **projectsProjectIdPublicationsIdMediaPost**
> MediaFileDto projectsProjectIdPublicationsIdMediaPost()


### Example

```typescript
import {
    PublicationsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublicationsApi(configuration);

let id: string; // (default to undefined)
let projectId: string; // (default to undefined)
let file: File; // (optional) (default to undefined)

const { status, data } = await apiInstance.projectsProjectIdPublicationsIdMediaPost(
    id,
    projectId,
    file
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **id** | [**string**] |  | defaults to undefined|
| **projectId** | [**string**] |  | defaults to undefined|
| **file** | [**File**] |  | (optional) defaults to undefined|


### Return type

**MediaFileDto**

### Authorization

[Bearer](../README.md#Bearer)

### HTTP request headers

 - **Content-Type**: multipart/form-data
 - **Accept**: text/plain, application/json, text/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
|**200** | OK |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **projectsProjectIdPublicationsIdRegeneratePost**
> PublicationDetailDto projectsProjectIdPublicationsIdRegeneratePost()


### Example

```typescript
import {
    PublicationsApi,
    Configuration,
    RegeneratePublicationRequest
} from './api';

const configuration = new Configuration();
const apiInstance = new PublicationsApi(configuration);

let id: string; // (default to undefined)
let projectId: string; // (default to undefined)
let regeneratePublicationRequest: RegeneratePublicationRequest; // (optional)

const { status, data } = await apiInstance.projectsProjectIdPublicationsIdRegeneratePost(
    id,
    projectId,
    regeneratePublicationRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **regeneratePublicationRequest** | **RegeneratePublicationRequest**|  | |
| **id** | [**string**] |  | defaults to undefined|
| **projectId** | [**string**] |  | defaults to undefined|


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

# **projectsProjectIdPublicationsIdRejectPost**
> PublicationDetailDto projectsProjectIdPublicationsIdRejectPost()


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
let projectId: string; // (default to undefined)
let rejectPublicationRequest: RejectPublicationRequest; // (optional)

const { status, data } = await apiInstance.projectsProjectIdPublicationsIdRejectPost(
    id,
    projectId,
    rejectPublicationRequest
);
```

### Parameters

|Name | Type | Description  | Notes|
|------------- | ------------- | ------------- | -------------|
| **rejectPublicationRequest** | **RejectPublicationRequest**|  | |
| **id** | [**string**] |  | defaults to undefined|
| **projectId** | [**string**] |  | defaults to undefined|


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

# **projectsProjectIdPublicationsIdSendPost**
> PublicationDetailDto projectsProjectIdPublicationsIdSendPost()


### Example

```typescript
import {
    PublicationsApi,
    Configuration
} from './api';

const configuration = new Configuration();
const apiInstance = new PublicationsApi(configuration);

let id: string; // (default to undefined)
let projectId: string; // (default to undefined)

const { status, data } = await apiInstance.projectsProjectIdPublicationsIdSendPost(
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

