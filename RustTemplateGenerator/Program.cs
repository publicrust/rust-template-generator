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

        var modeOption = new Option<GeneratorMode>(
            name: "--mode",
            description: "Generator mode (Full or UpdateOnly)",
            getDefaultValue: () => GeneratorMode.Full);

        var rootCommand = new RootCommand("Rust Template Generator - utility for generating Rust templates from .NET assemblies");
        rootCommand.AddOption(inputOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(modeOption);

        rootCommand.SetHandler(async (input, output, mode) =>
        {
            try
            {
                await ProcessFiles(input, output, mode);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                Environment.Exit(1);
            }
        }, inputOption, outputOption, modeOption);

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task ProcessFiles(DirectoryInfo inputDir, DirectoryInfo outputDir, GeneratorMode mode)
    {
        if (!inputDir.Exists)
        {
            throw new DirectoryNotFoundException($"Folder not found: {inputDir.FullName}");
        }

        // Проверяем наличие Newtonsoft.Json.dll в Managed папке
        var newtonsoftPath = Path.Combine(inputDir.FullName, "Newtonsoft.Json.dll");
        var needToAddNewtonsoft = !File.Exists(newtonsoftPath);
        
        // Если библиотеки нет, копируем её из NuGet кеша
        if (needToAddNewtonsoft)
        {
            var nugetCache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages",
                "newtonsoft.json",
                "13.0.3",
                "lib",
                "netstandard2.0",
                "Newtonsoft.Json.dll"
            );
            
            if (File.Exists(nugetCache))
            {
                AnsiConsole.MarkupLine("[yellow]Adding Newtonsoft.Json.dll temporarily...[/]");
                File.Copy(nugetCache, newtonsoftPath, overwrite: true);
            }
        }

        // For UpdateOnly mode, check if output directory exists
        if (mode == GeneratorMode.UpdateOnly && !outputDir.Exists)
        {
            throw new DirectoryNotFoundException($"Target directory not found: {outputDir.FullName}. Use Full mode to create new project.");
        }

        // For Full mode, handle directory cleanup and cloning
        if (mode == GeneratorMode.Full)
        {
            if (outputDir.Exists && outputDir.GetFileSystemInfos().Length > 0)
            {
                AnsiConsole.MarkupLine("[yellow]Cleaning existing directory...[/]");
                outputDir.Delete(true);
            }

            AnsiConsole.MarkupLine("[blue]Cloning repository...[/]");
            await CloneRepository(outputDir.FullName);
        }

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
        
        // Create .rust-analyzer directory if it doesn't exist
        if (!Directory.Exists(rustAnalyzerDir))
        {
            Directory.CreateDirectory(rustAnalyzerDir);
        }

        // Save hooks.json with analysis results
        var uniqueHooks = allHooks.Distinct().ToList();
        var hooksJson = JsonSerializer.Serialize(uniqueHooks, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(rustAnalyzerDir, "hooks.json"), hooksJson);

        // Clear other files
        await File.WriteAllTextAsync(Path.Combine(rustAnalyzerDir, "deprecatedHooks.json"), "[]");
        await File.WriteAllTextAsync(Path.Combine(rustAnalyzerDir, "stringPool.json"), "[]");

        // In Full mode, also update Managed folder
        if (mode == GeneratorMode.Full)
        {
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
        }

        // Удаляем временную копию Newtonsoft.Json.dll если мы её добавляли
        if (needToAddNewtonsoft && File.Exists(newtonsoftPath))
        {
            AnsiConsole.MarkupLine("[yellow]Removing temporary Newtonsoft.Json.dll...[/]");
            File.Delete(newtonsoftPath);
        }

        AnsiConsole.MarkupLine($"[green]Processing completed![/]");
        AnsiConsole.MarkupLine($"Results saved in: {outputDir.FullName}");
        AnsiConsole.MarkupLine($"[blue]Hooks found: {uniqueHooks.Count}[/]");
        AnsiConsole.MarkupLine($"[blue]Mode: {mode}[/]");
        
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