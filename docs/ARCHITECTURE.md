# Architecture

Progress Sync is a Jellyfin server plugin (`net10.0`, Jellyfin 12.1+) plus a small script that runs
in the web client. Copying and syncing happen on the server; the script only adds the button and
dialog.

```
jellyfin-web                                   Jellyfin server
 index.html ◄── File Transformation ◄────────── IndexTransformer (adds <script>)
 progressSync.js ─── GET  /ProgressSync/ClientScript
   button + dialog ─ REST /ProgressSync/* ────► ProgressSyncController
                                                  ├─ ProgressCopier   copy logic
                                                  └─ LinkStore        links.json
                                                LinkSyncService ◄── IUserDataManager.UserDataSaved
```

## Copying progress

`ProgressCopier` copies four fields of `UserItemData`: `PlaybackPositionTicks`, `Played`,
`PlayCount` and `LastPlayedDate`. Everything else (favorite, rating, stream choices) is kept from
the target. `Likes` is never assigned, because its setter overwrites `Rating`.

- **Scope.** Movie or episode: the item and all its versions. Season or series: its episodes from
  `Season/Series.GetEpisodes(user, …)`, enumerated as the target user. Missing episodes are skipped.
- **Access.** Every item is re-resolved with `ILibraryManager.GetItemById(id, user)` for both users,
  which applies library access and parental rules (`BaseItem.IsVisible` only checks parental
  rules). Items the source can't see are skipped, not reset.
- **Cache coherence.** User data is read with `GetUserDataBatch` (cache first), cloned before it is
  changed, and saved through the cached item instance, so later playback reports don't overwrite a
  copy with stale data.

## Links

A link is `{ Id, CreatorUserId, PartnerUserId, RootItemId, Mode, CreatedUtc }`. The root is the
series for episodes, seasons and series, and the primary version for movies.

| Mode    | Flow                | Visible to / removable by |
|---------|---------------------|---------------------------|
| Follow  | partner → creator   | creator                   |
| Mirror  | both ways           | creator and partner       |

Creating a link first copies the partner's progress for the whole root to the creator.

`LinkSyncService` subscribes to `IUserDataManager.UserDataSaved`. For an episode or movie save it
looks up links on the item's root and copies the saved data to every user reachable from the saving
user. The server raises the event synchronously on the saving thread, so the plugin's own writes
re-enter the handler on the same thread. A `[ThreadStatic]` set of users already written stops
loops and lets changes pass along chains of links, writing each user at most once. Saves with
reason `UpdateUserRating` or `Import` (NFO imports on temporary items) are ignored, and the original
save reason is kept so the server still throttles client notifications for playback progress.

## Storage

Links live in `<plugin configurations>/Jellyfin.Plugin.ProgressSync/links.json`, outside the
plugin's own folder so upgrades keep them. Reads use an immutable snapshot indexed by root item;
writes take a lock, write a temp file and swap it in with `File.Replace`, keeping `links.json.bak`.
A corrupt file falls back to the backup. A file that exists but can't be read puts the store in
read-only mode, so it is never overwritten. Links of deleted users are dropped on the next write.

## Web client

Server plugins have no supported way to extend jellyfin-web, so the plugin uses the File
Transformation plugin:

- `WebInjectionService` finds File Transformation through `AssemblyLoadContext` at startup and
  registers `IndexTransformer` for `index.html`. The payload is built through File Transformation's
  own `JObject` type, so the plugin doesn't ship Newtonsoft.Json.
- File Transformation also matches that key as a regex against other paths (for example
  `itemDetails-index-html.<hash>.chunk.js`). `IndexTransformer` therefore only modifies documents
  that start with `<!DOCTYPE html` or `<html`, and returns everything else unchanged.
- The script URL is relative (`../ProgressSync/ClientScript?v=<module version id>`), so it works
  with any base URL, and the version changes with every build.

`progressSync.js` listens for jellyfin-web's `viewshow` event on `#itemDetailPage`, adds the button
next to the other actions, and builds the dialog with `Dashboard.dialogHelper`. All user and item
names are inserted as text. The page reloads after a change because the details page only refreshes
part of itself when user data changes.

## REST API

All routes start with `/ProgressSync`. User routes act on the caller only; no route accepts a
target user.

| Route | Access | Purpose |
|---|---|---|
| `POST /Items/{itemId}/CopyFrom/{sourceUserId}` | user | Copy progress to the caller |
| `GET /Items/{itemId}/Links` | user | Links on the item's root visible to the caller |
| `POST /Items/{itemId}/Links` | user | Create or update a link, then copy the partner's progress |
| `DELETE /Links/{linkId}` | user | Remove a link the caller may remove (404 otherwise) |
| `GET /ClientScript` | anonymous | The web client script |
| `GET /Admin/Overview` | admin | Integration status and all links, for the dashboard page |

Requests without a user (such as API keys) get 403 on user routes.
