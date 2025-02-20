# Rust Template Generator

A command-line utility for generating Rust game plugin templates from .NET assemblies.

## Features

- Clones the base template from [rust-template](https://github.com/publicrust/rust-template)
- Analyzes DLL files from the Managed folder
- Extracts hooks information
- Generates hooks.json with found hooks
- Automatically filters out Newtonsoft.Json.dll
- Provides statistics about found hooks

## Prerequisites

- .NET 8.0 SDK
- Git installed and available in PATH
- Access to the Rust Dedicated Server's Managed folder

## Installation

1. Clone this repository:
```bash
git clone <your-repo-url>
cd rust-template-generator
```

2. Build the project:
```bash
dotnet build
```

## Usage

Basic usage:
```bash
dotnet run --project RustTemplateGenerator/RustTemplateGenerator.csproj --input <path-to-managed-folder> --output <output-path>
```

Example:
```bash
dotnet run --project RustTemplateGenerator/RustTemplateGenerator.csproj --input /path/to/RustDedicated_Data/Managed --output ./my-plugin
```

### Parameters

- `--input` (required): Path to the Managed folder containing DLL files
- `--output` (optional): Path where the generated template will be saved (default: ./output)

## Output Structure

The generator creates the following structure:
```
output/
├── .rust-analyzer/
│   ├── hooks.json         # Contains extracted hooks information
│   ├── deprecatedHooks.json
│   └── stringPool.json
├── Managed/              # Contains copied DLL files (excluding Newtonsoft.Json.dll)
└── ... other template files
```

## Progress Display

The utility shows:
- Progress bar during file processing
- Warning messages for any processing issues
- Statistics table with hooks count per file
- Total number of found hooks
- Final status and output location

## Building from Source

```bash
dotnet build -c Release
```

The compiled binary will be available in the `bin/Release/net8.0` directory.

## Contributing

1. Fork the repository
2. Create your feature branch
3. Commit your changes
4. Push to the branch
5. Create a new Pull Request

## License

This project is licensed under the MIT License - see the LICENSE file for details.