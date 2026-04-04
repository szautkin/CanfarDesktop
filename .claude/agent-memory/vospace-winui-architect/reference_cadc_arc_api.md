---
name: CADC ARC/Cavern VOSpace API Surface
description: Verified VOSpace 2.1 endpoint inventory for CADC's ARC storage service (Cavern implementation) at ws-uv.canfar.net/arc, sourced from opencadc/vos conformance tests and server source code
type: reference
---

Base URL: `https://ws-uv.canfar.net/arc`
Authority: `cadc.nrc.ca~arc`
VOSpace URI prefix: `vos://cadc.nrc.ca~arc/`
XML Namespace: `http://www.ivoa.net/xml/VOSpace/v2.0` (input/NodeReader) — note NodeWriter may use `http://www.opencadc.org/vospace/v20` in output

## Node Operations (REST)

### GET /nodes/{path} — List/read node
Query params:
- `detail=min|max|properties` — min strips properties, max adds computed read/write permissions
- `limit=N` — max child nodes to return (positive integer; 0 means no children)
- `uri={vosURI}` — cursor-based pagination start (must be a child URI of target container)
- `view=data` — returns 303 redirect to /files/{path}
- `sort`, `order` — NOT supported (throws UnsupportedOperationException)
Accept: `text/xml` or `application/json`
Response: 200 with VOSpace XML/JSON, 404 if not found

### PUT /nodes/{path} — Create node
Content-Type: `text/xml` (or `application/xml`)
Body: VOSpace node XML with `xsi:type` = `vos:ContainerNode`, `vos:DataNode`, or `vos:LinkNode`
Response: 201 Created
Errors: ContainerNotFound (parent missing), DuplicateNode, PermissionDenied
NOTE: parent must already exist, no auto-creation of intermediate nodes

### POST /nodes/{path} — Update node properties
Content-Type: `text/xml`
Body: VOSpace node XML with updated properties (use `xsi:nil="true"` to delete a property)
Response: 200

### DELETE /nodes/{path} — Delete single node
Response: 200 (or 204)
Errors: NodeNotFound, PermissionDenied, node locked
NOTE: For recursive delete of containers with children, use POST /async-delete instead

## File Access (files-proto capability, Cavern-specific shortcut)

### PUT /files/{path} — Upload file content
Content: raw file bytes (InputStream)
Content-Type: any (stored as node property)
Response: 201 with content-length: 0
Features: MD5 digest verification (412 on mismatch), quota enforcement
NOTE: This is a Cavern extension, not standard VOSpace 2.1

### GET /files/{path} — Download file content
Response: 200 with file bytes, 204 for empty files
Headers: Content-Disposition: inline; filename="name", content digest

### HEAD /files/{path} — File metadata only
Returns content-length, content-type, digest headers without body

## Transfer Negotiation (Standard VOSpace 2.1)
- `POST /transfers` — async UWS job for upload/download/move/copy
- `POST /synctrans` — sync transfer via params (?target=, &direction=, &protocol=)

## Bulk/Async Operations
- `POST /async-delete` — recursive delete (UWS job)
- `POST /async-setprops` — recursive property update (UWS job)
- `GET /pkg` — package download

## Utility
- `GET /capabilities` — VOSI capabilities XML
- `GET /availability` — health check

## Auth
Supports: anonymous (public nodes), cookie, TLS certificate, bearer token (`Authorization: Bearer {token}`)

## Standard Property URIs
- `ivo://ivoa.net/vospace/core#quota` — quota in bytes
- `ivo://ivoa.net/vospace/core#length` — size in bytes
- `ivo://ivoa.net/vospace/core#date` — last modified ISO timestamp
- `ivo://ivoa.net/vospace/core#creator` — owner display name
- `ivo://ivoa.net/vospace/core#type` — content type
- `ivo://ivoa.net/vospace/core#ispublic` — boolean
- `ivo://ivoa.net/vospace/core#isLocked` — boolean
- `ivo://ivoa.net/vospace/core#groupRead` — group URI
- `ivo://ivoa.net/vospace/core#groupWrite` — group URI
- `ivo://ivoa.net/vospace/core#inheritPermissions` — boolean

## XML Input Format (PUT /nodes)
```xml
<vos:node xmlns:vos="http://www.ivoa.net/xml/VOSpace/v2.0"
          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          uri="vos://cadc.nrc.ca~arc/home/{user}/{path}"
          xsi:type="vos:ContainerNode">
  <vos:properties/>
</vos:node>
```

## Key Observations (verified 2026-04-01)
- PUT /files is confirmed working in Cavern (PutAction.java exists), bypasses transfer negotiation
- Pagination is cursor-based via `uri` param, NOT offset-based
- `limit=0` suppresses child listing entirely (used for quota-only requests)
- macOS app only implements getQuota; Windows app is ahead with full CRUD
- The `sort` and `order` query params are NOT implemented server-side — sorting must be done client-side
