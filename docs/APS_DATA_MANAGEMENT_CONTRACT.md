# APS Data Management Contract — Read-Only Browsing, Recursive Search, Cloud-Model GUID Resolution

> Verified against the official `autodesk-platform-services/aps-sdk-net` generated Data
> Management SDK source (the Autodesk documentation site at aps.autodesk.com renders its
> reference pages client-side and could not be scraped directly by automated tooling in this
> session — the same limitation already noted in `APS_AUTHENTICATION_CONTRACT.md`) and the
> official Autodesk Platform Services blog. Access dates: 2026-07-30 (browsing), 2026-07-31
> (recursive search, GUID resolution). This document is the source of truth for
> `SAUDICO.Federate.ACC.DataManagement` — do not diverge from it without re-verifying against the
> SDK source or official docs.

## Scope

Read-only GET browsing, recursive search, and cloud-model identifier *verification* only: hubs →
a hub's projects → a project's top folders → a folder's contents (folders + items) → recursive RVT
search (current folder+subfolders, or every top folder of a project) → verifying (never guessing)
whether a search result is a genuine Revit Cloud Model, all with pagination. No POST/PUT/PATCH/
DELETE. No cloud-model *opening*, no download, no upload/rename/move/delete/publish — resolving a
model's identifiers does not open it; that remains a separate, not-yet-implemented step (see
`PROJECT_AUDIT.md`'s Next task).

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
| Recursive folder search | GET | `/data/v1/projects/{project_id}/folders/{folder_id}/search` | Yes (`Search.Links` is `PaginationInfo`, same shape as Projects) |

All five require `Authorization: Bearer <access_token>` and `Accept: application/json` — identical
transport to Authentication v2's `/userinfo` call, reused via the existing `IApsHttpTransport`.

## Recursive search endpoint (verified against `FoldersApi.gen.cs`'s `GetFolderSearchAsync`)

Official doc comment (`GetFolderSearchAsync`, method name "List Folder and Subfolder Contents"):
"Searches the specified folder and its subfolders and returns a list of the latest versions of the
items you can access. ... It returns tip versions (latest versions) of properties where the filter
conditions are satisfied." Two consequences, both load-bearing for this implementation:

- **Recursion into subfolders is inherent to the endpoint** — `SearchFolderRecursiveAsync` calls it
  exactly once per (project, folder) pair; it is not implemented by walking subfolders manually.
- **Only tip/latest versions are ever returned** — "search only for latest/tip versions" required no
  extra filter parameter; it is the endpoint's default, unconditional behavior. No `filter[...]`
  query parameter is sent by this client — narrowing to `.rvt` is applied client-side instead (same
  reasoning as the folder-contents RVT filter above: avoids guessing a filter field/value pair this
  session could not fully verify).

### Response shape — the reverse cross-reference of folder-contents

Verified directly against the `Search` model (no `Search.gen.cs` file exists by that name in the
SDK's Model directory; its shape comes from the `ApiResponse<Search>` return type of
`GetFolderSearchAsync`, whose properties were read directly): `Search.Data` is `List<VersionData>`,
`Search.Included` is `List<ItemData>` — i.e. **the opposite direction** from
`FolderContents` (there, `data` was folders/items and `included` was versions). For search:

- `data[]` are **version** resources (`type == "versions"`, `VersionAttributes`): `id` (the version
  id), `attributes.versionNumber`, `attributes.lastModifiedTime`, `attributes.displayName`, and
  `relationships.item.data.id` (verified via `VersionDataRelationships.Item` →
  `VersionDataRelationshipsItem`) pointing to the owning item.
- `included[]` are **item** resources (`type == "items"`, `ItemAttributes`), cross-referenced by
  that `relationships.item.data.id`: `attributes.displayName` (the item's canonical name — used
  over the version's own `displayName` when both are present), `attributes.pathInProject`, and
  `relationships.parent.data.id` (the containing folder id).
- `attributes.pathInProject`'s exact doc comment: "The relative path of the item starting from
  project's root folder." This is used **verbatim** as the search result's `FolderPath` — the API
  supplies it directly, so it is never constructed or guessed locally (unlike a naive approach that
  would try to reassemble a path from ancestor folder names, which the search response does not
  reliably provide for every ancestor depth).
- An item with no resolvable owning item in `included[]` is skipped, not guessed at.

### Entire-project search

Not a single endpoint — `SearchProjectAsync` calls the existing `GetTopFoldersAsync` (reused
verbatim, not reimplemented) to enumerate a project's top folders, then calls
`SearchFolderRecursiveAsync` once per top folder with bounded concurrency
(`SemaphoreSlim`, max 3 concurrent), merging results and deduplicating by `ItemId` (never display
name — the SDK guarantees `id` uniqueness per item, not `displayName`). If one top folder's search
fails (e.g. 403 — no access to that specific top-level container), the others' results are still
returned and `AccSearchOutcome.HasPartialFailure` is set; cancellation, by contrast, aborts the
whole operation immediately rather than being treated as a per-folder failure to route around.

## Pagination hardening (applies to every paginated endpoint above, not duplicated per-endpoint)

Two defensive checks were added to the single shared `CollectPagesAsync`/`GetNextPageUrl` helper
used by all five endpoints:

- **Unsafe pagination hosts are rejected.** `links.next.href` is only followed when it parses as an
  absolute `https://` URL whose host is exactly `developer.api.autodesk.com`; anything else is
  logged and treated as "no more pages" rather than fetched.
- **Repeated-next-link protection.** Each fetched URL is recorded in a per-call `HashSet`; if a
  `next` link repeats a URL already fetched in this same paginated call, pagination stops (keeping
  whatever was already collected) instead of looping forever.

## Cloud-model classification and GUID resolution (see `AccCloudModelClassifier`)

`AccResolutionStatus` has three values: `CloudModelVerified`, `UploadedFile`, `Unresolved`.

**`CloudModelVerified`** requires ALL of the following to be true of the tip version resource:
- `attributes.extension.type` equals exactly `versions:autodesk.bim360:C4RModel` — the official
  Revit Cloud Model / Revit Cloud Worksharing (C4R) marker, verified against the official
  Autodesk Platform Services blog post "Accessing BIM 360 Design models on Revit" and corroborated
  independently.
- `attributes.extension.data.projectGuid` is present and parses as a valid GUID
  (`Guid.TryParse`).
- `attributes.extension.data.modelGuid` is present and parses as a valid GUID.

Both fields live in `attributes.extension.data` — confirmed structurally against the SDK's own
`VersionExtensionWithSchemaLink.Data` property, which is typed `Dictionary<string, object>` (not a
fixed schema), because extension payload shape varies per item type. This client reads
`projectGuid`/`modelGuid` as loose string fields from that dictionary, exactly as the official blog
documents, via `System.Text.Json` — never decoding, computing, or guessing them from the item/
version id.

Only when all three conditions hold does `AccCloudSourceDescriptor.ProjectGuid`/`ModelGuid` get
populated — with the **exact string values APS returned**, never reformatted or re-derived. Any one
condition failing (wrong/absent extension type, missing GUID, or a GUID that doesn't parse) falls
back to `Unresolved` rather than guessing — including a matching extension type with a garbled
GUID. `UploadedFile` is returned for the one plain-file `attributes.extension.type` this session
could verify against APS's official filter-parameter documentation, `items:autodesk.bim360:File`.
Getting a classification "wrong" in the conservative direction (`Unresolved` instead of
`UploadedFile`, or `Unresolved` instead of `CloudModelVerified` on a technicality) is always safe —
`Unresolved` and `UploadedFile` both equally block federation. Only fabricating `CloudModelVerified`
without full proof would be unsafe, and the classifier structurally cannot do that: it is a pure
function of three already-parsed, already-validated inputs, with no fallback path that invents a
`CloudModelVerified` result.

**Verifying identifiers is not the same as being able to open the model.** Even a fully
`CloudModelVerified` job is still refused by the federation queue in this build
(`AccQueueHelper.HasAnyAccJob`) — actually opening/exporting an ACC-sourced Revit Cloud Model
through `ModelPathUtils.ConvertCloudGUIDsToCloudPath` and the `ExternalEvent` pipeline is a
separate, deliberately not-yet-implemented step (see `PROJECT_AUDIT.md`'s Next task). This
increment's job is limited to determining, honestly, whether that would even be possible for a
given search result.

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

## Documentation sources consulted (access dates 2026-07-30 / 2026-07-31)

- https://github.com/autodesk-platform-services/aps-sdk-net (source-verified: `datamanagement/source/Http/HubsApi.gen.cs`, `ProjectsApi.gen.cs`, `FoldersApi.gen.cs`; `datamanagement/source/Model/HubDataAttributes.gen.cs`, `Hubs.gen.cs`, `ProjectDataAttributes.gen.cs`, `Projects.gen.cs`, `PaginationInfo.gen.cs`, `JsonApiLinksSelf.gen.cs`, `TopFolders.gen.cs`, `FolderAttributesWithExtensions.gen.cs`, `FolderContents.gen.cs`, `FolderContentsLinks.gen.cs`, `JsonApiLink.gen.cs`, `ItemAttributes.gen.cs`, `ItemData.gen.cs`, `ItemDataRelationships.gen.cs`, `JsonApiRelationshipsLinksToTipVersion.gen.cs`, `VersionData.gen.cs`, `VersionDataRelationships.gen.cs`, `VersionAttributes.gen.cs`)
- https://aps.autodesk.com/en/docs/data/v2/reference/http/hubs-GET
- https://aps.autodesk.com/en/docs/data/v2/reference/http/hubs-hub_id-projects-GET
- https://aps.autodesk.com/en/docs/data/v2/reference/http/projects-project_id-folders-folder_id-contents-GET
- https://aps.autodesk.com/en/docs/data/v2/reference/http/projects-project_id-folders-folder_id-search-GET
- https://get-started.aps.autodesk.com/tutorials/hubs-browser/data/ (confirms the general hubs→projects→folders browsing shape; does not itself document the `included`/tip-version cross-reference, which was instead verified directly against the SDK's typed model above)
- WebSearch summary referencing APS's `filter[extension.type]` documentation for the `items:autodesk.bim360:File` example value (used only as a conservative, safe-direction classification signal — see "Cloud-model classification" above; not used to assert any cloud-model extension type, which this session could not verify with equivalent confidence)
- https://aps.autodesk.com/blog/accessing-bim-360-design-models-revit (official APS blog: `attributes.extension.data.projectGuid`/`.modelGuid` on the GET Version response, used with `ModelPathUtils.ConvertCloudGUIDsToCloudPath` — the primary source for the GUID-resolution contract above)
- https://raw.githubusercontent.com/autodesk-platform-services/aps-sdk-net/main/datamanagement/source/Model/VersionExtensionWithSchemaLink.gen.cs (source-verified: `Data` property is `Dictionary<string, object>`, confirming `extension.data` is a loosely-typed, per-item-type payload rather than a fixed schema — corroborates reading `projectGuid`/`modelGuid` as generic string fields)
- WebSearch summary corroborating the `versions:autodesk.bim360:C4RModel` extension type as the marker Autodesk uses on the version resource for Revit Cloud Worksharing (C4R) models, alongside the `attributes.extension.data.projectGuid`/`.modelGuid` fields
- https://www.revitapidocs.com/2022/aa710231-4cab-98ba-951f-00c72e06bb6e.htm (`ModelPathUtils.ConvertCloudGUIDsToCloudPath(string region, Guid projectGuid, Guid modelGuid)` signature — confirms the region/projectGuid/modelGuid parameter shape this resolution work targets; not itself invoked in this increment)
