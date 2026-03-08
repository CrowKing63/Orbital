# Orbit Tests

This directory contains unit tests for the Orbit application.

## Running Tests

From the repository root:

```bash
dotnet test Orbit.Tests/Orbit.Tests.csproj
```

From the Orbit.Tests directory:

```bash
dotnet test
```

## Test Coverage

The test suite currently covers:

- **ActionGatingTests**: Tests for action selection requirement logic
  - Validates that actions correctly determine if text selection is required
  - Tests default behavior for different action types (Paste vs others)
  - Verifies explicit RequiresSelection overrides

- **SettingsManagerTests**: Tests for settings management
  - Default settings creation and structure
  - API key encryption/decryption round-trip
  - Empty and null API key handling
  - Built-in action configuration validation

- **ActionExecutorServiceTests**: Tests for action execution service
  - Service initialization with and without LLM service
  - Selection-required action gating
  - LLM action validation when no service is configured

## Test Framework

Tests use xUnit as the testing framework.
