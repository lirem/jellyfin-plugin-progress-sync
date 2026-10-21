# Releasing

1. Update `changelog` in `build.yaml` (and `targetAbi` plus the `Jellyfin.*` package versions if
   the release targets a newer Jellyfin).
2. On GitHub, draft a new release with a tag like `v1.0.0.0` (four-part version, always increasing)
   and publish it. Pre-releases are ignored.
3. The **Release** workflow:
   - runs the tests;
   - builds `progress-sync_<version>.zip` with
     [JPRM](https://github.com/oddstr13/jellyfin-plugin-repository-manager);
   - attaches the zip to the release;
   - adds the version to `manifest.json` on `main`.
4. Check that
   `https://raw.githubusercontent.com/lirem/jellyfin-plugin-progress-sync/main/manifest.json`
   lists the new version. Servers that added the repository then see the update in their catalog.

If `main` is protected, allow GitHub Actions to push to it, or the manifest step fails.

## Listing on jellyfin.org

Third-party repositories are listed on
[jellyfin.org's plugin page](https://jellyfin.org/docs/general/server/plugins/). To be listed,
open a pull request to [jellyfin/jellyfin.org](https://github.com/jellyfin/jellyfin.org) that:

- adds an entry to `ThirdPartyRepositories` in `src/data/pluginRepositories.ts`:

  ```ts
  {
    id: 'gh:lirem/jellyfin-plugin-progress-sync',
    name: "lirem's Repo",
    url: 'https://raw.githubusercontent.com/lirem/jellyfin-plugin-progress-sync/main/manifest.json',
    includes: {
      'Progress Sync': 'https://github.com/lirem/jellyfin-plugin-progress-sync'
    }
  },
  ```

- adds a short `#### Progress Sync` entry, with a GitHub link, under "3rd-Party Plugins" in
  `docs/general/server/plugins/index.mdx` (alphabetical order).

Publish at least one release before opening the pull request.
