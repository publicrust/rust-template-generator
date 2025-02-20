using System.CommandLine;
using System.Text.Json;
using System.Diagnostics;
using Library;
using Library.Models;
using RustTemplateGenerator.Models;
using Spectre.Console;

namespace RustTemplateGenerator;

class Program
{
    private const string REPO_URL = "https://github.com/publicrust/rust-template.git";

    static async Task<int> Main(string[] args)
    {
        var inputOption = new Option<DirectoryInfo>(
            name: "--input",
            description: "Path to the Managed folder with DLL files")
        {
            IsRequired = true
        };

        var outputOption = new Option<DirectoryInfo>(
            name: "--output",
            description: "Path to save results",
            getDefaultValue: () => new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "output")));

        var rootCommand = new RootCommand("Rust Template Generator - utility for generating Rust templates from .NET assemblies");
        rootCommand.AddOption(inputOption);
        rootCommand.AddOption(outputOption);

        rootCommand.SetHandler(async (input, output) =>
        {
            try
            {
                await ProcessFiles(input, output);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                Environment.Exit(1);
            }
        }, inputOption, outputOption);

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task ProcessFiles(DirectoryInfo inputDir, DirectoryInfo outputDir)
    {
        if (!inputDir.Exists)
        {
            throw new DirectoryNotFoundException($"Folder not found: {inputDir.FullName}");
        }

        // If directory exists and not empty, delete it
        if (outputDir.Exists && outputDir.GetFileSystemInfos().Length > 0)
        {
            AnsiConsole.MarkupLine("[yellow]Cleaning existing directory...[/]");
            outputDir.Delete(true);
        }

        // Clone repository to output directory
        AnsiConsole.MarkupLine("[blue]Cloning repository...[/]");
        await CloneRepository(outputDir.FullName);

        AnsiConsole.MarkupLine("[blue]Starting file processing...[/]");

        var allHooks = new List<HookImplementationModel>();
        var dllFiles = Directory.GetFiles(inputDir.FullName, "*.dll", SearchOption.TopDirectoryOnly);

        if (!dllFiles.Any())
        {
            throw new Exception($"No DLL files found in folder: {inputDir.FullName}");
        }

        var progress = AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn()
            });

        await progress.StartAsync(ctx =>
        {
            var task = ctx.AddTask("[green]Processing files[/]", maxValue: dllFiles.Length);

            foreach (var dllFile in dllFiles)
            {
                try
                {
                    var hooksDictionary = AssemblyDataSerializer.FindHooksDictionary(dllFile);

                    if (hooksDictionary.Count > 0)
                    {
                        var implementationModels = hooksDictionary.Values.Select(hook => new HookImplementationModel
                        {
                            HookSignature = hook.HookSignature,
                            MethodSignature = hook.MethodSignature,
                            MethodSourceCode = hook.MethodCode,
                            MethodClassName = hook.ClassName,
                            HookLineInvoke = hook.LineNumber
                        });

                        allHooks.AddRange(implementationModels);
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning while processing {Path.GetFileName(dllFile)}: {ex.Message}[/]");
                }

                task.Increment(1);
            }

            return Task.CompletedTask;
        });

        // Update files in .rust-analyzer
        var rustAnalyzerDir = Path.Combine(outputDir.FullName, ".rust-analyzer");
        
        // Save hooks.json with analysis results
        var uniqueHooks = allHooks.Distinct().ToList();
        var hooksJson = JsonSerializer.Serialize(uniqueHooks, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(rustAnalyzerDir, "hooks.json"), hooksJson);

        // Clear other files
        await File.WriteAllTextAsync(Path.Combine(rustAnalyzerDir, "deprecatedHooks.json"), "[]");
        await File.WriteAllTextAsync(Path.Combine(rustAnalyzerDir, "stringPool.json"), "[]");

        // Clean and copy new DLL files
        var managedDir = Path.Combine(outputDir.FullName, "Managed");
        if (Directory.Exists(managedDir))
        {
            Directory.Delete(managedDir, true);
        }
        Directory.CreateDirectory(managedDir);

        foreach (var dllFile in dllFiles)
        {
            var fileName = Path.GetFileName(dllFile);
            // Skip Newtonsoft.Json.dll
            if (fileName.Equals("Newtonsoft.Json.dll", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine($"[yellow]Skipping file {fileName}[/]");
                continue;
            }
            var targetPath = Path.Combine(managedDir, fileName);
            File.Copy(dllFile, targetPath, overwrite: true);
        }

        AnsiConsole.MarkupLine($"[green]Processing completed![/]");
        AnsiConsole.MarkupLine($"Results saved in: {outputDir.FullName}");
        AnsiConsole.MarkupLine($"[blue]Hooks found: {uniqueHooks.Count}[/]");
        
        // Display file statistics
        var table = new Table()
            .AddColumn("File")
            .AddColumn("Hooks Count");

        var hooksPerFile = allHooks.GroupBy(h => h.MethodClassName)
            .Select(g => new { ClassName = g.Key, Count = g.Count() });

        foreach (var stat in hooksPerFile)
        {
            table.AddRow(stat.ClassName, stat.Count.ToString());
        }

        AnsiConsole.Write(table);
    }

    private static async Task CloneRepository(string outputPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"clone {REPO_URL} \"{outputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = startInfo };
        
        try 
        {
            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new Exception($"Error cloning repository: {error}");
            }

            // Check that all files were cloned
            var files = Directory.GetFiles(outputPath, "*.*", SearchOption.AllDirectories);

            if (!files.Any())
            {
                throw new Exception("Repository cloned, but files are missing");
            }

            AnsiConsole.MarkupLine($"[green]Files cloned: {files.Length}[/]");
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to clone repository: {ex.Message}");
        }
    }
} 