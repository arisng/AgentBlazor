---
name: ab-release
description: "Release workflow and versioning for AgentBlazor. Use when creating releases, updating versions, publishing packages, or managing private feeds. Covers versioning strategy, release notes, and publishing workflows."
---

# AgentBlazor Release Workflow

## Versioning Strategy

AgentBlazor uses **semver** with the following pattern:

- **Major**: Breaking changes (e.g., `1.0.0`)
- **Minor**: New features (e.g., `0.2.24`)
- **Patch**: Bug fixes (e.g., `0.2.24-internal.1`)

**Current version**: `0.2.24-internal.1` (private build)

## Version Updates

1. **Update version** in `Directory.Build.props`:
   ```xml
   <Version>0.2.24-internal.1</Version>
   ```

2. **Update package references** if needed

3. **Update documentation** with new version information

## Release Notes

1. **Create release notes** in `docs/releases/`:
   - File naming: `{version}.md` (e.g., `0.2.24-internal.1.md`)
   - Include: changes, new features, bug fixes, breaking changes

2. **Update README.md** with latest version information

## Publishing Workflows

### Private Feed Publishing

1. **Use the script**:
   ```bash
   ./scripts/publish-private-feed.ps1
   ```

2. **Feed location**: `AGENTBLAZOR_LOCAL_FEED` environment variable

3. **Folder structure**:
   - Root: Flat mirror of current version
   - Version folder: Archive + exact-version source

### Public Publishing

1. **Build packages**:
   ```bash
   dotnet pack
   ```

2. **Publish to NuGet**:
   ```bash
   dotnet nuget push *.nupkg --source https://api.nuget.org/v3/index.json
   ```

## Release Checklist

- [ ] Update version in `Directory.Build.props`
- [ ] Create release notes in `docs/releases/`
- [ ] Update documentation
- [ ] Run all tests
- [ ] Build packages
- [ ] Publish to private feed (for internal builds)
- [ ] Publish to NuGet (for public releases)
- [ ] Update CHANGELOG.md (if exists)

## Version Management

1. **Private builds**: Use `-internal.1` suffix
2. **Public releases**: Remove suffix for public versions
3. **Upstream sync**: Be cautious with version conflicts during merge

## Package Structure

AgentBlazor publishes multiple packages:

- `AgentBlazor` — Main package
- `AgentBlazor.Core` — Core services
- `AgentBlazor.Components` — Blazor components
- `AgentBlazor.Hosting` — ASP.NET Core hosting
- `AgentBlazor.EntityFrameworkCore` — EF Core integration
- `AgentBlazor.Cli` — CLI tool
- `AgentBlazor.Licensing` — Licensing tiers
- `AgentBlazor.ProviderAdapters` — Provider adapters

## Troubleshooting

1. **Version conflicts**: Check `Directory.Build.props` for version
2. **Build failures**: Run `dotnet restore` before build
3. **Package issues**: Verify package references in `Directory.Packages.props`
4. **Feed issues**: Check `AGENTBLAZOR_LOCAL_FEED` environment variable