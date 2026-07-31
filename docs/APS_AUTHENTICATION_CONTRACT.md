# APS Authentication Contract — Authorization Code Grant with PKCE

> Verified against official Autodesk Platform Services (APS) documentation, the official
> `autodesk-platform-services/aps-sdk-net` generated SDK source (Authentication module), and the
> official Revit 2025 API Developers Guide / API reference for the Revit-hosting constraints.
> Access date: 2026-07-29. This document is the source of truth for `SAUDICO.Federate.ACC`'s
> authentication implementation and for how it may run inside the Revit 2025 host — do not diverge
> from it without re-verifying against Autodesk.

## API status

- Authentication API **v2** is the current, non-deprecated API. v1 (`/authentication/v1/...`) is
  deprecated per Autodesk's "Migration guide - OAuth2 v1 to v2".
- "Desktop, Mobile, Single-Page App" is an officially supported APS application type — these are
  **public clients** (no client secret) and use Authorization Code Grant with **PKCE**, per
  Autodesk's "New application types" announcement and the "Getting a Token with PKCE - Desktop App"
  guidance.
- The generated `Autodesk.Authentication` NuGet package (part of `aps-sdk-net`) targets **.NET 8+
  only** and therefore cannot be referenced from the Revit 2024 (`net48`) target. SAUDICO Federate
  therefore implements its own minimal, dependency-light OAuth/PKCE client using `HttpClient`,
  cross-compiled for `net48` and `net8.0-windows`, instead of taking a package dependency on the
  official SDK. The endpoint/parameter contract below was cross-checked directly against that SDK's
  source to avoid drift from the official implementation.

## Endpoints

| Purpose | Method | URL |
|---|---|---|
| Authorization | GET | `https://developer.api.autodesk.com/authentication/v2/authorize` |
| Token exchange / refresh | POST | `https://developer.api.autodesk.com/authentication/v2/token` |
| User profile (OIDC UserInfo) | GET | `https://api.userprofile.autodesk.com/userinfo` |

Token endpoint content type: `application/x-www-form-urlencoded`.
User-profile request: `Authorization: Bearer <access_token>` header, `Accept: application/json`.

## Authorization request (GET /authorize)

Required query parameters for this app's flow:

| Parameter | Value |
|---|---|
| `response_type` | `code` (verbatim) |
| `client_id` | the app's APS Client ID |
| `redirect_uri` | the exact registered callback URI, URL-encoded |
| `scope` | space-separated scope list (see Scopes below) |
| `state` | app-generated CSRF/session-binding value |
| `code_challenge` | Base64URL(SHA-256(code_verifier)), no padding |
| `code_challenge_method` | `S256` (verbatim) |

The SDK source also supports optional `nonce`, `response_mode`, `prompt`, `authoptions` parameters;
none are required for this app's flow and none are used.

## Token exchange (POST /token) — `grant_type=authorization_code`

Public/native-app clients (this app's registration type) send `client_id` in the form body and use
**no** `Authorization: Basic` header and **no client secret** — confirmed directly in the SDK's
`TokenApi` source: the Basic-auth header is only added when a non-empty `authorization` (i.e.
Base64 `client_id:client_secret`) value is supplied, which this app never does.

| Form field | Value |
|---|---|
| `grant_type` | `authorization_code` (verbatim) |
| `client_id` | the app's APS Client ID |
| `code` | the authorization code from the callback |
| `redirect_uri` | the exact same URI used in the authorize request |
| `code_verifier` | the original PKCE verifier (43–128 chars, never persisted) |

## Token refresh (POST /token) — `grant_type=refresh_token`

| Form field | Value |
|---|---|
| `grant_type` | `refresh_token` (verbatim) |
| `client_id` | the app's APS Client ID |
| `refresh_token` | the current refresh token |
| `scope` | optional; omit to keep the original grant's scopes |

**Refresh-token rotation**: Autodesk issues a **new** `refresh_token` on every refresh (confirmed by
Autodesk's "About Refresh Token" guidance and the SDK's token model). The old refresh token becomes
invalid once rotated. The app **must** persist the replacement token every time and must not assume
the original refresh token remains usable.

## Token response fields (`ThreeLeggedToken` model, SDK-verified)

| Field | Type | Notes |
|---|---|---|
| `token_type` | string | Always `"Bearer"`. |
| `access_token` | string | Bearer access token. |
| `expires_in` | integer | Seconds until expiry, relative to issuance. |
| `refresh_token` | string | Present for 3-legged flows; rotates on every refresh. |
| `id_token` | string | Present only if `openid` scope was requested. |

`ExpiresAtUtc` is derived locally as `DateTime.UtcNow + expires_in seconds` at the moment the
response is received — Autodesk does not return an absolute expiry timestamp for the v2 3-legged
flow in this SDK model, so the app computes and stores it immediately on receipt.

## Error response fields

Standard OAuth2 error shape: `error` (e.g. `invalid_grant`, `invalid_request`, `access_denied`,
`invalid_scope`) and `error_description`. `invalid_grant` on a refresh attempt means the refresh
token is no longer valid (rotated elsewhere, revoked, or expired) — the app must clear stored
authentication and require interactive sign-in again.

## Callback exact-match behavior

The `redirect_uri` sent in both the authorize request and the token exchange must **byte-exact
match** one of the callback URIs registered on the APS application, including scheme, host, port,
path, and trailing slash. A mismatch (including a missing/extra trailing slash) is rejected by
Autodesk. SAUDICO Federate uses a single fixed loopback URI, `http://localhost:8080/` (root path,
trailing slash included), and never varies it at runtime — used identically by
`config/apssettings.json`, the merged effective configuration, the authorize `redirect_uri`, the
token-exchange `redirect_uri`, and `LocalOAuthCallbackListener`'s bound prefix (verified end-to-end
2026-07-29; this superseded an earlier draft value of
`http://localhost:8080/api/auth/callback/` that was never actually registered or shipped — corrected
here to match the real registered/configured value).

## Verified minimum scopes used by SAUDICO Federate

| Scope (wire value) | Purpose |
|---|---|
| `data:read` | Read-only access, reserved for the future Data Management (hubs/projects) client. Not exercised by this milestone. |
| `user-profile:read` | Read the signed-in user's profile fields returned by `/userinfo`. |
| `openid` | Required for OIDC `/userinfo` access and to receive `id_token`. |

No write, create, delete, or admin scope (`data:write`, `data:create`, `bucket:*`, `account:write`,
`code:all`, etc.) is requested or permitted by configuration validation.

## Access-token lifetime behavior

`expires_in` is short-lived (on the order of an hour; exact value is returned per-token and must
not be hardcoded). The app treats a token as invalid once within a safety margin of its computed
`ExpiresAtUtc` and proactively refreshes rather than waiting for a 401.

## Revit 2025 hosting contract (verified against official Autodesk Revit API documentation)

These are the Revit-side (not APS-side) rules that constrain how and where the authentication code
above may run inside SAUDICO Federate's Revit 2025 host. Verified 2026-07-29 against the official
Revit 2025 API Developers Guide on `help.autodesk.com` (URL structure and page titles confirmed via
search; like the APS site, this portal renders content client-side and could not be fetched
directly by automated tooling in this session — the same limitation already noted below for the APS
documentation) and the official generated API reference on `revitapidocs.com`.

- **Revit 2025 add-ins are .NET 8-only.** The Revit 2025 API Developers Guide's "Migrating From
  .NET 4.8 to .NET 8" page states the Revit 2025 API is based on .NET Core 8 and add-ins must be
  recompiled for .NET 8 — there is no .NET Framework compatibility shim. `SAUDICO.Federate.Revit2025`
  and every project it loads (`Shared`, `Core`, `Export`, `UI`, `SAUDICO.Federate.ACC`,
  `SAUDICO.Federate.Logging`) target `net8.0-windows` for this build, driven by the shared
  `RevitYear`-conditional `TargetFramework` in `Directory.Build.props`; `Revit2024`/net48 remains a
  separate, non-loaded target for the 2024 host only.
- **`UIApplication.MainWindowHandle` is the correct WPF/modal owner handle.** Per the Revit API
  reference, this property was added specifically to replace
  `System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle`, which became unreliable once
  Revit's docking system changed (Revit 2019 onward) — `MainWindowHandle` is the property Autodesk
  documents as safe for parenting add-in dialogs. `FederationCommand.Execute` captures
  `commandData.Application.MainWindowHandle` once and passes it into `Manager`, which uses the same
  `IntPtr` (via `WindowInteropHelper(window).Owner = ...`) to own every ACC browser window
  (`DiagnosticShellWindow`, both `AccBrowserWindow` constructors) — never `Process.MainWindowHandle`.
- **`ExternalEvent`/`IExternalEventHandler` exists to marshal calls *into* the Revit API from
  modeless/external code — it is not a general-purpose async mechanism.** Per the Revit API
  Developers Guide's "External Events" page, the pattern is: implement `IExternalEventHandler`,
  register it via `ExternalEvent.Create(...)`, and call `Raise()` only when the modeless UI needs to
  perform an actual Revit API operation, which Revit then executes on the next Idling cycle inside a
  valid API context. Authentication, PKCE, the local OAuth callback listener, HTTP calls to APS, and
  user-profile retrieval touch **no** Revit API surface at all, so none of them use `ExternalEvent` —
  it remains reserved exclusively for the existing NWC export job queue (`RequestHandler`/
  `RevitWorkItem` in `Host.cs`). No Revit API call is made from a background thread or from inside
  any ACC/authentication callback.

## Documentation sources consulted (access date 2026-07-29)

- https://help.autodesk.com/view/RVT/2025/ENU/?guid=Revit_API_Revit_API_Developers_Guide_Introduction_Getting_Started_Using_the_Autodesk_Revit_API_NET8_Update_html ("Migrating From .NET 4.8 to .NET 8")
- https://help.autodesk.com/view/RVT/2025/ENU/?guid=Revit_API_Revit_API_Developers_Guide_Advanced_Topics_External_Events_html ("External Events")
- https://www.revitapidocs.com/2025/51ca80e2-3e5f-7dd2-9d95-f210950c72ae.htm (`UIApplication` class reference, incl. `MainWindowHandle`)
- https://thebuildingcoder.typepad.com/blog/2018/11/revit-window-handle-and-parenting-an-add-in-form.html (background on why `MainWindowHandle` replaced `Process.MainWindowHandle`)

- https://aps.autodesk.com/en/docs/oauth/v2/tutorials/get-3-legged-token-pkce
- https://aps.autodesk.com/en/docs/oauth/v2/tutorials/get-3-legged-token-pkce/get-3-legged-token-pkce-private
- https://aps.autodesk.com/en/docs/oauth/v2/tutorials/code-challenge
- https://aps.autodesk.com/en/docs/oauth/v2/reference/http/authorize-GET
- https://aps.autodesk.com/en/docs/oauth/v2/reference/http/gettoken-POST/
- https://aps.autodesk.com/en/docs/oauth/v2/developers_guide/basics
- https://aps.autodesk.com/en/docs/oauth/v2/developers_guide/scopes/
- https://aps.autodesk.com/blog/getting-token-pkce-desktop-app-0
- https://aps.autodesk.com/blog/new-application-types
- https://aps.autodesk.com/blog/about-refresh-token
- https://aps.autodesk.com/blog/migration-guide-oauth2-v1-v2
- https://aps.autodesk.com/blog/new-viewablesread-scope
- https://aps.autodesk.com/developer/overview/authentication-api
- https://github.com/autodesk-platform-services/aps-sdk-net (source-verified: `authentication/source/Http/TokenApi.gen.cs`, `UsersApi.gen.cs`, `authentication/source/Model/ThreeLeggedToken.gen.cs`, `UserInfo.gen.cs`, `Scopes.gen.cs`, `GrantType.gen.cs`)

Autodesk's own documentation site renders its parameter tables client-side (React SPA), which
automated fetching cannot extract directly; the generated `.NET` SDK source was used to
cross-verify every endpoint path, parameter name, and response field listed above against the
current, official, non-deprecated implementation rather than guessing from prose alone.

## Conflicts with the originating task prompt

None found. The task prompt's required authorization/token fields (`response_type=code`,
`client_id`, `redirect_uri`, `scope`, `state`, `code_challenge`, `code_challenge_method=S256`;
`grant_type=authorization_code` with `code`, `redirect_uri`, `code_verifier`, no client secret) match
the verified official contract exactly.
