---
name: ab-testing
description: "Testing patterns and conventions for AgentBlazor projects. Use when writing unit tests, integration tests, or debugging test failures. Covers xUnit, bUnit, coverlet, test organization, and project-specific testing conventions."
---

# AgentBlazor Testing Patterns

## Test Framework

- **xUnit** as the test framework
- **bUnit** for Blazor component testing
- **coverlet** for code coverage
- **Xunit.SkippableFact** for conditional tests

## Test Projects

| Project | Coverage |
|---------|----------|
| `AgentBlazor.Core.Tests` | Core services, attributes, runtime, conversation, middleware, tools |
| `AgentBlazor.Components.Tests` | Component rendering, markdown, handoff, chat surfaces |
| `AgentBlazor.IntegrationTests` | End-to-end workflow tests, provider wire capture, reasoning effort |
| `AgentBlazor.Cli.Analysis.Tests` | CLI analysis |
| `AgentBlazor.Cli.IntegrationTests` | CLI integration |
| `tests/e2e/` | End-to-end tests |

## Conventions

1. **Target framework**: Tests target `net10.0`
2. **Internal visibility**: Test projects use `InternalsVisibleTo` for testing internal members
3. **Test naming**: Use descriptive method names (e.g., `Should_Return_Success_When_Valid_Input`)
4. **Assertions**: Use xUnit assertions (`Assert.Equal`, `Assert.True`, etc.)
5. **Async tests**: Use `async Task` for asynchronous operations
6. **Test data**: Use `[Theory]` with `[InlineData]` or `[MemberData]` for parameterized tests
7. **Conditional tests**: Use `Skip.If()` from `Xunit.SkippableFact` for conditional execution

## Running Tests

```bash
# Run all tests
dotnet test AgentBlazor.sln --configuration Debug

# Run specific test project
dotnet test tests/AgentBlazor.Core.Tests/AgentBlazor.Core.Tests.csproj

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## bUnit Testing

For Blazor component testing:

```csharp
using Bunit;
using Xunit;

public class MyComponentTests : TestContext
{
    [Fact]
    public void Should_Render_Component()
    {
        // Arrange & Act
        var cut = RenderComponent<MyComponent>();
        
        // Assert
        cut.MarkupMatches("<div>Hello World</div>");
    }
}
```

## Integration Testing

Integration tests in `AgentBlazor.IntegrationTests`:
- Test provider wire capture
- Test reasoning effort options
- Test end-to-end workflows
- Reference the demo project directly

## Common Patterns

1. **Mocking**: Use mocking frameworks if needed (check existing tests for patterns)
2. **Dependency injection**: Use `Microsoft.Extensions.DependencyInjection` for test services
3. **Async operations**: Always await async operations in tests
4. **Cleanup**: Implement `IDisposable` or `IAsyncDisposable` for test cleanup

## Debugging Tests

1. Use `dotnet test --logger "console;verbosity=detailed"` for detailed output
2. Check test output for assertion failures
3. Use debugging tools in IDE for step-by-step debugging
4. Review coverage reports for untested code paths