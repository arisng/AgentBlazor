---
name: ab-contribution
description: "Contributor workflows for AgentBlazor. Use when setting up development environment, creating pull requests, following coding standards, or understanding the fork model. Covers development setup, code review, and contribution guidelines."
---

# AgentBlazor Contribution Guidelines

## Development Setup

1. **Prerequisites**:
   - .NET 10.0 SDK (or later)
   - Git
   - IDE (Visual Studio, VS Code, or Rider)

2. **Clone the repository**:
   ```bash
   git clone https://github.com/arisng/AgentBlazor.git
   cd AgentBlazor
   ```

3. **Build the project**:
   ```bash
   dotnet build
   ```

4. **Run tests**:
   ```bash
   dotnet test AgentBlazor.sln --configuration Debug
   ```

## Fork Model

This is a **fork** (`arisng/AgentBlazor`) from upstream (`ashpeterson/AgentBlazor`) using mirror/merge model:

- **`master`** — clean mirror of `upstream/master`. Never receives local commits.
- **`develop`** — all divergence lives here. Merge the refreshed mirror in periodically.

**Important**: See [`DIVERGENCE.md`](DIVERGENCE.md) for tracked divergence points.

## Branching Strategy

1. **Feature branches**: Create from `develop` for new features
2. **Bug fixes**: Create from `develop` for bug fixes
3. **Upstream sync**: Use `git-fork-sync` skill to sync with upstream

## Coding Standards

1. **Nullable enabled**: All projects have `<Nullable>enable</Nullable>`
2. **Implicit usings**: Enabled across all projects
3. **Namespace conventions**: `AgentBlazor.*` root namespace, sub-namespaces mirror folder structure
4. **Internal visibility**: Tests use `InternalsVisibleTo` for testing internal members
5. **CSS isolation**: Component CSS files with `.razor.css` pattern

## Pull Request Process

1. **Create a feature branch** from `develop`
2. **Make changes** following coding standards
3. **Write tests** for new functionality
4. **Run tests** to ensure they pass
5. **Update documentation** if needed
6. **Create pull request** to `develop` branch
7. **Code review** by maintainers

## Testing Requirements

1. **Unit tests**: Write tests for new services and components
2. **Integration tests**: Test end-to-end workflows
3. **Coverage**: Aim for good code coverage (use `coverlet`)
4. **bUnit tests**: For Blazor component testing

## Documentation

1. **Update README.md** for new features or setup changes
2. **Add documentation** in `docs/` for new features
3. **Update AGENTS.md** for new patterns or conventions
4. **Add skills** in `.github/skills/` for new workflows

## Release Process

1. **Version updates**: Update version in `Directory.Build.props`
2. **Release notes**: Add release notes in `docs/releases/`
3. **Private feed**: Use `scripts/publish-private-feed.ps1` for private builds
4. **Public release**: Follow upstream release process for public releases

## Getting Help

1. **Documentation**: Check `docs/` for guides and references
2. **Skills**: Use `.github/skills/` for workflow automation
3. **Issues**: Create GitHub issues for bugs or feature requests
4. **Discussions**: Use GitHub discussions for questions