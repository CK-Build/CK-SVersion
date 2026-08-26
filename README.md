# CK-SVersion

**CK-SVersion** is the [CK-Build](https://github.com/CK-Build) library implementing `SVersion`: a
version type that fully conforms to [Semantic Versioning 2.0.0](https://semver.org/), plus a stricter
"Conformant SVersion" subset (with well-defined CI/post-release builds, exploratory versions, and a
mathematically sound version-range model) that CKli and the CK-Build ecosystem rely on to reason about
package versions.

The single library project is [`CK.SVersion`](CK.SVersion) — see its
**[README](CK.SVersion/README.md)** for the full picture: the `SVersion` type itself, the
"Conformant SVersion" rules (stable / prerelease / exploratory / CI-build forms), the `SVersionBound`
version-range lattice, and the `CSVersionKindFilter` used to route packages to feeds.

Tests live in [`Tests/CK.SVersion.Tests`](Tests/CK.SVersion.Tests).

This repository is a dependency of [`CKli`](../CKli/README.md) — see the
[Stack-level README](../README.md) for how it fits into this Stack.
