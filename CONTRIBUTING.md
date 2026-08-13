# Contributing to Manifestor

Thanks for considering a contribution. Manifestor is developed as a Unity package inside a small Unity host project.

## Development setup

1. Install Unity 6000.5.3f1 through Unity Hub. The package supports Unity 6000.4 or newer, while this repository is serialized with 6000.5.3f1.
2. Clone the repository and open its root folder as a Unity project.
3. Allow Unity Package Manager to restore the versions recorded in `Packages/packages-lock.json`.
4. Open **Window > General > Test Runner**, select **EditMode**, and run the `com.mirovsky.manifestor.tests` assembly.

The distributable package is in `SharedPackages/com.mirovsky.manifestor`. Content under `Assets` is the development host and contains example profiles and extension code used for manual verification.

## Making changes

- Keep runtime-independent package code Editor-only.
- Preserve existing serialized field names unless a migration is included.
- Put Unity event methods such as `Awake`, `OnEnable`, and `Update` immediately below fields in Unity classes.
- Add or update tests when changing behavior, and keep unrelated formatting or generated project-file changes out of the pull request.
- Update `CHANGELOG.md` and the package changelog when a user-visible change is intended for a release.

Before opening a pull request, run the EditMode tests, search for stale references after renames, and confirm Unity has not added `Library`, `Temp`, IDE metadata, or generated schema files to the change set.

For vulnerabilities, follow [SECURITY.md](SECURITY.md) instead of opening a public issue.
