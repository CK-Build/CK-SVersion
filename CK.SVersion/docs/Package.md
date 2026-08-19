Implements `SVersion` class that fully conforms to https://semver.org/ (v2.0.0) with extensions to
support a 4th part (`1.0.0.0` is valid) and a notion of "Conformant SVersion" that is a subset of SemVer versions.

A `SVersionBound` is a lattice element that describes a version range constraint that can be parsed from NuGet and Npm version
ranges syntaxes.
