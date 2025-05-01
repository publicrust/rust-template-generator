# Rust Template Generator

Utility for generating Rust game plugin templates with hooks extraction.

## Step 1: Generate Template

```bash
# Clone and build
git clone https://github.com/publicrust/rust-template-generator
cd rust-template-generator
dotnet build

# Generate template
dotnet run --project RustTemplateGenerator/RustTemplateGenerator.csproj --input /path/to/RustDedicated_Data/Managed --output ./my-plugin --mode Full 
```

--mode Full or UpdateOnly

## Step 2: Generate StringPool

1. Create `StringPoolDumper.cs` in `oxide/plugins`:

```csharp
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("StringPool Dumper", "RustGPT", "1.0.0")]
    [Description("Dumps StringPool.toNumber dictionary to a JSON file")]
    public class StringPoolDumper : RustPlugin
    {
        private const string FileName = "stringpool_dump.json";

        private void OnServerInitialized(bool initial)
        {
            DumpStringPool();
        }

        private void DumpStringPool()
        {
            _ = StringPool.Get("");
            Interface.Oxide.DataFileSystem.WriteObject(FileName, StringPool.toNumber);
            Puts($"StringPool dumped to {FileName} in oxide/data directory");
        }
    }
}
```

2. Start server and wait for initialization
3. Copy generated file:
```bash
cp oxide/data/stringpool_dump.json <output-path>/.rust-analyzer/stringPool.json
```

## Output Structure

```
output/
├── .rust-analyzer/
│   ├── hooks.json         # Extracted hooks
│   ├── deprecatedHooks.json
│   └── stringPool.json    # From server
├── Managed/              # DLL files
└── ... template files
```

## License

MIT License