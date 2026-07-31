# APS Data Management Contract — Read-Only Browsing (Hubs → Projects → Folders)

> Verified against the official `autodesk-platform-services/aps-sdk-net` generated Data
> Management SDK source (the Autodesk documentation site at aps.autodesk.com renders its
> reference pages client-side and could not be scraped directly by automated tooling in this
> session — the same limitation already noted in `APS_AUTHENTICATION_CONTRACT.md`). Access date:
> 2026-07-30. This document is the source of truth for `SAUDICO.Federate.ACC.DataManagement` — do
> not diverge from it without re-verifying against the SDK source or official docs.

## Scope

Read-only GET browsing only: hubs → a hub's projects → a project's top folders → a folder's
contents (folders + items), with pagination. No POST/PUT/PATCH/DELETE. No model resolution, no
cloud-model opening, no download, no upload/rename/move/delete/publish.

## Base host

`https://developer.api.autodesk.com` — the same host already used for Authentication v2
(`APS_AUTHENTICATION_CONTRACT.md`).

## Endpoints (verified against `datamanagement/source/Http/*.gen.cs`)

| Purpose | Method | URL | Paginated? |
|---|---|---|---|
| List hubs | GET | `/project/v1/hubs` | No (`JsonApiLinksSelf` — `self` only, no `next`) |
| List a hub's projects | GET | `/project/v1/hubs/{hub_id}/projects` | Yes (`PaginationInfo.Next.Href`) |
| List a project's top folders | GET | `/project/v1/hubs/{hub_id}/projects/{project_id}/topFolders` | No (`JsonApiLinksSelf`) |
| List a folder's contents | GET | `/data/v1/projects/{project_id}/folders/{folder_id}/contents` | Yes (`FolderContentsLinks.Next.Href`) |

All four require `Authorization: Bearer <access_token>` and `Accept: application/json` — identical
transport to Authentication v2's `/userinfo` call, reused via the existing `IApsHttpTransport`.

## Response shape (JSON:API), verified against `datamanagement/source/Model/*.gen.cs`

Every response has `data` (array) and, for the paginated endpoints, `links.next.href`
(`JsonApiLink.Href`, an absolute URL — the pagination-following code requests this URL verbatim,
never reconstructing query parameters itself).

- Hub (`HubData`/`HubDataAttributes`): `data[].id`, `data[].type == "hubs"`,
  `data[].attributes.name`.
- Project (`ProjectData`/`ProjectDataAttributes`): `data[].id`, `data[].type == "projects"`,
  `data[].attributes.name`.
- Folder, from either `topFolders` (`TopFolderData`) or folder-contents
  (`FolderAttributesWithExtensions`): `data[].id`, `data[].type == "folders"`,
  `data[].attributes.name` — **not** `attributes.displayName`, which the SDK's own doc comment
  marks "Reserved for future Use. Do not use." for folders.
- Item, only present in folder-contents (`ItemAttributes`): `data[].id`,
  `data[].type == "items"`, `data[].attributes.displayName` (the correct, documented field for an
  item's name — unlike folders, `displayName` **is** the right field here),
  `data[].attributes.lastModifiedTime`.
- Version number/last-modified for an item: `FolderContents.Included` is typed
  `List<VersionData>` in the SDK — i.e. the folder-contents response's top-level `included` array
  is specifically version resources for the returned items' tip versions, not a generic mixed bag.
  An item's tip version id is `data[].relationships.tip.data.id`
  (`ItemDataRelationships.Tip` → `JsonApiRelationshipsLinksToTipVersion.Data` →
  `JsonApiTypeId.Id`); cross-referencing that id against `included[].id` (where
  `included[].type == "versions"`) gives `included[].attributes.versionNumber` (int) and
  `included[].attributes.lastModifiedTime` via `VersionAttributes`.

## RVT filtering

The task scope is "folders; Revit model items with .rvt names" — a filename-suffix filter
(`displayName` ends with `.rvt`, case-insensitive), not an `attributes.extension.type` check. This
was a deliberate choice, not an oversight: `extension.type` values differ across BIM 360 Docs, ACC,
and Fusion Team environments, and matching against the literal, portable ".rvt" name requirement is
both what was asked for and avoids guessing at an extension-type enumeration this session could not
verify against every possible source project type.

## Error handling

The existing `IApsHttpTransport.GetJsonAsync` already throws `ApsApiException` (with `StatusCode`
and `ApsErrorCode`) for any non-2xx response or `HttpRequestException` — reused as-is, no new HTTP
plumbing was added for Data Management. `AccDataManagementErrorMapper` maps `StatusCode` to a fixed
user-facing string: 401→session expired, 403→insufficient access, 404→item unavailable,
429→rate-limited, 5xx→service unavailable, no status (network failure)→connection unavailable.

## Token refresh on 401

Reuses the existing `IApsAuthenticationService` abstraction exclusively — `AccDataManagementClient`
never reads, stores, or constructs a token itself. On a 401 it calls the (new, additive)
`IApsAuthenticationService.RefreshAccessTokenAsync` — which forces an actual refresh-token grant
call through the same gated, already-tested refresh flow `GetValidAccessTokenAsync` uses
internally, bypassing only the "is the cached token still within its safety margin" fast-path check
— and retries the same GET exactly once with the new token. If the refresh itself fails, or the
retried GET still returns 401, the client calls the existing `SignOutAsync` (best-effort, local
only) so the UI returns to Signed Out; any other status code on the retry (403/404/429/5xx/network)
is surfaced as its own mapped error without forcing a sign-out.

## Documentation sources consulted (access date 2026-07-30)

- https://github.com/autodesk-platform-services/aps-sdk-net (source-verified: `datamanagement/source/Http/HubsApi.gen.cs`, `ProjectsApi.gen.cs`, `FoldersApi.gen.cs`; `datamanagement/source/Model/HubDataAttributes.gen.cs`, `Hubs.gen.cs`, `ProjectDataAttributes.gen.cs`, `Projects.gen.cs`, `PaginationInfo.gen.cs`, `JsonApiLinksSelf.gen.cs`, `TopFolders.gen.cs`, `FolderAttributesWithExtensions.gen.cs`, `FolderContents.gen.cs`, `FolderContentsLinks.gen.cs`, `JsonApiLink.gen.cs`, `ItemAttributes.gen.cs`, `ItemDataRelationships.gen.cs`, `JsonApiRelationshipsLinksToTipVersion.gen.cs`, `VersionData.gen.cs`, `VersionAttributes.gen.cs`)
- https://aps.autodesk.com/en/docs/data/v2/reference/http/hubs-GET
- https://aps.autodesk.com/en/docs/data/v2/reference/http/hubs-hub_id-projects-GET
- https://aps.autodesk.com/en/docs/data/v2/reference/http/projects-project_id-folders-folder_id-contents-GET
- https://get-started.aps.autodesk.com/tutorials/hubs-browser/data/ (confirms the general hubs→projects→folders browsing shape; does not itself document the `included`/tip-version cross-reference, which was instead verified directly against the SDK's typed model above)
