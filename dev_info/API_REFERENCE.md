# CANFAR API Reference (for Desktop Client)

> Extracted from the Next.js science-portal-next source code.
> Source files: `src/lib/api/skaha.ts`, `src/lib/api/storage.ts`, `src/lib/api/login.ts`, `src/app/api/**`

---

## Base URLs (CANFAR Mode)

| Service | Default Base URL | Config Key |
|---|---|---|
| CADC Login/Auth | `https://ws-cadc.canfar.net/ac` | `LoginBaseUrl` |
| Skaha | `https://ws-uv.canfar.net/skaha` | `SkahaBaseUrl` |
| Storage (ARC) | `https://ws-uv.canfar.net/arc/nodes/home` | `StorageBaseUrl` |
| CADC AC (users) | `https://ws-uv.canfar.net/ac` | `AcBaseUrl` |

All requests require: `Authorization: Bearer {token}` header (except login).

---

## 1. Authentication

### Login
```
POST https://ws-cadc.canfar.net/ac/login
Content-Type: application/x-www-form-urlencoded

Body: username={username}&password={password}

Response 200: base64-encoded token string (plain text body)
Response 401: Invalid credentials
```

### WhoAmI (verify token)
```
GET https://ws-cadc.canfar.net/ac/whoami
Authorization: Bearer {token}

Response 200: username string (plain text)
Response 401: Token expired/invalid
```

### Get User Details
```
GET {BaseUrl}/ac/users/{username}
Authorization: Bearer {token}
Accept: application/json

Response 200:
{
  "username": "string",
  "email": "string",
  "firstName": "string",
  "lastName": "string",
  "institute": "string",
  "internalID": "string",
  "identities": [{ "type": "string", "value": "string|number" }]
}
```

---

## 2. Sessions

### List All Sessions
```
GET {SkahaApi}/v1/session
Authorization: Bearer {token}

Response 200:
[
  {
    "id": "abc123",
    "userid": "username",
    "runAsUID": "1000",
    "runAsGID": "1000",
    "supplementalGroups": [1000],
    "image": "images.canfar.net/project/name:tag",
    "type": "notebook|desktop|carta|headless|contributed|firefly",
    "status": "Running|Pending|Terminating|Failed|Error",
    "name": "my-session",
    "startTime": "2024-01-15T10:30:00Z",
    "expiryTime": "2024-01-22T10:30:00Z",
    "connectURL": "https://ws-uv.canfar.net/session/notebook/abc123",
    "requestedRAM": "8G",
    "requestedCPUCores": "2",
    "requestedGPUCores": "0",
    "ramInUse": "2.5G",
    "cpuCoresInUse": "0.5",
    "isFixedResources": true
  }
]
```

### Launch Session
```
POST {SkahaApi}/v1/session
Authorization: Bearer {token}
Content-Type: application/x-www-form-urlencoded

Body: name={name}&image={imageId}&cores={cores}&ram={ramGB}&gpus={gpus}&type={type}

Optional body params: cmd={cmd}&env={json-env}
For contributed sessions with custom registry: replicas=1&registryUsername={u}&registrySecret={s}

Response 200: session ID string (plain text, or JSON array with session ID)
Response 400: Invalid parameters
Response 409: Session name conflict
```

### Get Single Session
```
GET {SkahaApi}/v1/session/{sessionId}
Authorization: Bearer {token}

Response 200: single session object (same schema as list item)
```

### Delete Session
```
DELETE {SkahaApi}/v1/session/{sessionId}
Authorization: Bearer {token}

Response 200: success
Response 404: Session not found
```

### Renew Session (extend expiry)
```
POST {SkahaApi}/v1/session/{sessionId}/renew
Authorization: Bearer {token}

Response 200: success (expiry extended by default period)
```

### Get Session Events
```
GET {SkahaApi}/v1/session/{sessionId}?view=events
Authorization: Bearer {token}

Response 200: text/plain — Kubernetes events as text
```

### Get Session Logs
```
GET {SkahaApi}/v1/session/{sessionId}?view=logs
Authorization: Bearer {token}

Response 200: text/plain — container logs
```

---

## 3. Container Images

### List Available Images
```
GET {SkahaApi}/v1/image
Authorization: Bearer {token}

Response 200:
[
  {
    "id": "images.canfar.net/skaha/notebook-scipy:1.0",
    "types": ["notebook"]
  },
  {
    "id": "images.canfar.net/skaha/desktop-xfce:1.0",
    "types": ["desktop", "contributed"]
  }
]
```

Image ID format: `{registry}/{project}/{name}:{version}`
Example: `images.canfar.net/canucs/canucs-notebook:1.2.3`

### Get Image Repositories
```
GET {SkahaApi}/v1/repository
Authorization: Bearer {token}

Response 200: ["images.canfar.net", "harbor.canfar.net"]
```

---

## 4. Session Context (Resource Options)

```
GET {SkahaApi}/v1/context
Authorization: Bearer {token}

Response 200:
{
  "cores": {
    "default": 2,
    "options": [1, 2, 4, 8, 16],
    "defaultRequest": "2",
    "availableValues": ["1","2","4","8","16"]
  },
  "memoryGB": {
    "default": 8,
    "options": [1, 2, 4, 8, 16, 32],
    "defaultRequest": "8",
    "availableValues": ["1","2","4","8","16","32"]
  },
  "gpus": {
    "options": [0, 1, 2]
  }
}
```

---

## 5. Platform Load (Stats)

```
GET {SkahaApi}/v1/stats
Authorization: Bearer {token}

Response 200:
{
  "instances": {
    "session": 15,
    "desktopApp": 5,
    "headless": 3,
    "total": 23
  },
  "cores": {
    "requestedCPUCores": 120,
    "cpuCoresAvailable": 500,
    "maxCPUCores": {
      "notebook": 16,
      "desktop": 16,
      "headless": 16,
      "carta": 8,
      "contributed": 16
    }
  },
  "ram": {
    "requestedRAM": "480G",
    "ramAvailable": "2000G",
    "maxRAM": {
      "notebook": "192G",
      "desktop": "192G",
      "headless": "192G",
      "carta": "192G",
      "contributed": "192G"
    }
  }
}
```

---

## 6. Storage (VOSpace/Cavern)

### Get User Storage Quota
```
GET {StorageApi}/nodes/home/{username}
Authorization: Bearer {token}
Accept: text/xml

Response 200: VOSpace XML document
```

The response is XML. Relevant properties extracted via parsing:

```xml
<vos:node xmlns:vos="http://www.ivoa.net/xml/VOSpace/v2.0" uri="vos://cadc.nrc.ca~arc/home/{username}">
  <vos:properties>
    <vos:property uri="ivo://ivoa.net/vospace/core#quota">{quotaBytes}</vos:property>
    <vos:property uri="ivo://ivoa.net/vospace/core#length">{usedBytes}</vos:property>
    <vos:property uri="ivo://ivoa.net/vospace/core#date">{lastModified}</vos:property>
  </vos:properties>
</vos:node>
```

Parse: `quota` and `length` (size) in bytes → convert to GB for display.

---

## Error Response Patterns

Most Skaha errors return plain text or JSON:

```
HTTP 401: Unauthorized — token expired or invalid
HTTP 403: Forbidden — insufficient permissions
HTTP 404: Not Found — session/resource doesn't exist
HTTP 409: Conflict — e.g., session name already exists
HTTP 500: Internal Server Error — server-side failure
```

For 401 responses: prompt re-login.

---

## Session Types

| Type | Icon | Description |
|---|---|---|
| `notebook` | Jupyter icon | Jupyter notebook environment |
| `desktop` | Desktop icon | VNC remote desktop |
| `carta` | CARTA icon | CARTA astronomical image viewer |
| `contributed` | Contributed icon | Community-contributed apps (VS Code, etc.) |
| `firefly` | Firefly icon | Firefly image viewer |
| `headless` | — | Headless batch processing |

---

## Notes for C# Implementation

1. **Form-urlencoded bodies** — Use `FormUrlEncodedContent` for login and session launch.
2. **Plain text responses** — Login and launch return plain text, not JSON. Use `ReadAsStringAsync()`.
3. **XML parsing** — Storage quota returns XML. Use `System.Xml.Linq.XDocument` to parse.
4. **RAM format** — Skaha returns RAM as strings like `"8G"`, `"2.5G"`. Parse numeric portion.
5. **Image ID parsing** — Split on `/` and `:` to extract registry, project, name, version.
6. **Token format** — CADC tokens are base64-encoded strings, used as-is in `Bearer` header.
