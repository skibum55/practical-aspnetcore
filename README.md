# Samples for ASP.NET Core

Forked from [github.com/dodyg/practical-aspnetcore](https://github.com/dodyg/practical-aspnetcore) for AI training on unit test creation.

Start with the first [open telemetry](/projects/open-telemetry/open-telemetry-1/) project.  Once you have sufficient coverage (80-90%) move to the more complex projects in the higher numbered folders (2-5.)

## Our Testing Toolkit

- **Test framework**: NUnit 4.x
- **Mocking**: Moq 4.x
- **Assertions**: NUnit's `Assert.That` (constraint-based)

## Key Testing Rules

1. **Transaction isolation** - Each test runs in its own transaction (rolled back in TearDown)
2. **Moq for services** - Mock service interfaces when testing controllers and external dependencies
3. **Assert.That always** - Use NUnit constraint model, not classic `Assert.AreEqual`
4. **Test file naming** - `{ClassUnderTest}Tests.cs`
5. **One assert concept per test** - Multiple `Assert.That` calls are OK if they verify one concept
6. **Arrange-Act-Assert** - Clear three-section structure in every test
7. **CommitChanges after setup** - Call `Session.CommitChanges()` after creating test entities

## Original README (Shortened)

Greetings from Cairo, Egypt. You can [sponsor](https://github.com/sponsors/dodyg) this project [here](https://github.com/sponsors/dodyg). 

## ASP.NET Core 10

You can find samples on new features availabel in ASP.NET Core 10(12) [here](/projects/net10). Datastar examples (20) can be found [here](/projects/datastar).

## ASP.NET Core 9

You can find samples on new features available in ASP.NET Core 9(3) [here](/projects/net9).

## Previous versions

[6.0](https://github.com/dodyg/practical-aspnetcore/tree/net6.0/), [5.0](https://github.com/dodyg/practical-aspnetcore/tree/net5.0/), [3.1 LTS](https://github.com/dodyg/practical-aspnetcore/tree/3.1-LTS/), [2.1 LTS](https://github.com/dodyg/practical-aspnetcore/tree/2.1-LTS)

## Other Samples

- For ATProtocol (the underlying open protocol for Bluesky) related samples, you can find them [here](https://github.com/dodyg/bluenile). 
- For Hydro Framework (Razor Pages compatible), you can find them [here](/projects/hydro/)(8).
- [Official .NET Aspire samples](https://github.com/dotnet/aspire-samples).
- For Data Access samples, go to the excellent [ORM Cookbook](https://github.com/Grauenwolf/DotNet-ORM-Cookbook).
- .NET team also has [a sample repository](https://github.com/dotnet/samples).

## How to run these samples

To run these samples, simply open your command line console, go to each folder and execute `dotnet watch run`.

## Misc

-   [Contributor Guidelines](https://github.com/dodyg/practical-aspnetcore/blob/master/CONTRIBUTING.md)
-   [Code of Conduct](https://github.com/dodyg/practical-aspnetcore/blob/master/CODE_OF_CONDUCT.md)
