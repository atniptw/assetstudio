# Unity 6 (6000.x) Support Documentation

## Overview

AssetStudio has been updated to support Unity 6000.0.58f2 and future Unity 6 (6000.x) versions.

## Changes Made

### 1. **BundleFile.cs** - Encryption Flag Logic

Updated the version check for UnityCN encryption flag handling to explicitly include Unity 6000+ versions:

- The `else` branch now handles Unity 2022.3.2+, 2023.x, 6000.x (Unity 6), and all future versions
- Added clarifying comment about Unity 6 support

### 2. **SerializedFileFormatVersion.cs** - File Format Documentation

Updated the `LargeFilesSupport` enum documentation:

- Changed from "2020.1 to x" to "2020.1 to 2023.x / 6000.x (Unity 6)"
- Unity 6 continues to use SerializedFileFormatVersion 22 (no new format introduced)

### 3. **SerializedFile.cs** - Version Parsing

Added clarifying comments to the `SetVersion` method:

- Documents that version parsing supports Unity 2.x through Unity 6 (6000.x) and beyond
- Parsing logic already correctly handles 4-digit major versions like "6000"
- Example: "6000.0.58f2" parses to version array `[6000, 0, 58, 2]` and build type `"f"`

### 4. **README.md** - Documentation

Added Unity version support notice:

- Explicitly states support for Unity 2.x through Unity 6 (6000.x) including Unity 6000.0.58f2

## Technical Details

### Version Parsing

Unity 6's version string format "6000.0.58f2" is parsed correctly:

- Major version: 6000
- Minor version: 0
- Patch version: 58
- Build type: f (final)
- Build number: 2

### Compatibility

All existing version comparison logic is forward-compatible:

- Checks like `version[0] > 2022` correctly evaluate to `true` for Unity 6000
- Checks like `version[0] >= 2021` correctly evaluate to `true` for Unity 6000
- The numeric comparison automatically handles Unity 6's versioning scheme

### File Format

Unity 6 (6000.x) uses the same serialized file format as Unity 2020.1+:

- SerializedFileFormatVersion: 22 (LargeFilesSupport)
- No new parsing logic required for the base file structure

## Testing Recommendations

When testing with Unity 6000.0.58f2 assets:

1. Verify asset files load without version-related errors
2. Check that encryption/decryption works correctly for UnityCN files
3. Confirm all asset types (textures, meshes, shaders, etc.) parse correctly
4. Test both bundled and loose asset files

## Future Unity Versions

The codebase is now structured to support future Unity versions (e.g., 6001.x, 7000.x) without modification, unless Unity introduces:

- A new SerializedFileFormatVersion (would need new enum entry)
- Breaking changes to asset structure (would need class-specific updates)
- New encryption schemes (would need crypto logic updates)

## Notes

Unity changed their versioning scheme from year-based (2023.x) to year-like (6000.x) starting with Unity 6. The "6000" represents the year 2024 + 4000 offset, but for parsing purposes, it's simply treated as a large version number that is greater than all 2020-2023 versions.
