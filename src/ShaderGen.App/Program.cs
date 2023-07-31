using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Reflection;
using CommandLine;
using ShaderGen.Glsl;
using ShaderGen.Hlsl;
using ShaderGen.Metal;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Dxc;

namespace ShaderGen.App
{
    internal static class Program
    {
        private class Options
        {
            [Option('r', "ref", Required = true, HelpText = "The semicolon-separated list of references to compile against.")]
            public string ReferenceItemsResponsePath { get; set; }

            [Option('s', "src", Required = true, HelpText = "The semicolon-separated list of source files to compile.")]
            public string CompileItemsResponsePath { get; set; }

            [Option('o', "out", Required = true, HelpText = "The output path for the generated shaders.")]
            public string OutputPath { get; set; }

            [Option('g', "genlist", Required = true, HelpText = "The output file to store the list of generated files.")]
            public string GenListFilePath { get; set; }

            [Option('l', "listall", Required = false, HelpText = "Forces all generated files to be listed in the list file. By default, only bytecode files will be listed and not the original shader code.")]
            public bool ListAllFiles { get; set; }

            [Option('p', "processor", Required = false, HelpText = "The path of an assembly containing IShaderSetProcessor types to be used to post-process GeneratedShaderSet objects.")]
            public string ProcessorPath { get; set; }

            [Option("processorargs", Required = false, HelpText = "Custom information passed to IShaderSetProcessor.")]
            public string ProcessorArgs { get; set; }

            [Option('d', "debug", Required = false, HelpText = "Compiles the shader with debug information when supported.")]
            public bool Debug { get; set; }
        }

        private static string s_fxcPath;
        private static bool? s_fxcAvailable;
        private static bool? s_glslangValidatorAvailable;

        private static bool? s_metalMacOSToolsAvailable;
        private static string s_metalMacPath;
        private static string s_metallibMacPath;

        private static bool? s_metaliOSAvailable;
        private static string s_metaliOSPath;
        private static string s_metallibiOSPath;

        const string metalBinPath = @"/usr/bin/metal";
        const string metallibBinPath = @"/usr/bin/metallib";

        public static int Main(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = args[i].Replace("\\\\", "\\");
            }

            var res = Parser.Default.ParseArguments<Options>(args);
            var opt = res.Value;
            if (opt == null)
            {
                Console.Error.WriteLine("Could not parse arguments!");
                return -1;
            }

            opt.ReferenceItemsResponsePath = NormalizePath(opt.ReferenceItemsResponsePath);
            opt.CompileItemsResponsePath = NormalizePath(opt.CompileItemsResponsePath);
            opt.OutputPath = NormalizePath(opt.OutputPath);
            opt.GenListFilePath = NormalizePath(opt.GenListFilePath);
            opt.ProcessorPath = NormalizePath(opt.ProcessorPath);

            if (!File.Exists(opt.ReferenceItemsResponsePath))
            {
                Console.Error.WriteLine("Reference items response file does not exist: " + opt.ReferenceItemsResponsePath);
                return -1;
            }
            if (!File.Exists(opt.CompileItemsResponsePath))
            {
                Console.Error.WriteLine("Compile items response file does not exist: " + opt.CompileItemsResponsePath);
                return -1;
            }
            if (!Directory.Exists(opt.OutputPath))
            {
                try
                {
                    Directory.CreateDirectory(opt.OutputPath);
                }
                catch
                {
                    Console.Error.WriteLine($"Unable to create the output directory \"{opt.OutputPath}\".");
                    return -1;
                }
            }

            string[] referenceItems = File.ReadAllLines(opt.ReferenceItemsResponsePath);
            string[] compileItems = File.ReadAllLines(opt.CompileItemsResponsePath);

            List<MetadataReference> references = new List<MetadataReference>();
            foreach (string referencePath in referenceItems)
            {
                if (!File.Exists(referencePath))
                {
                    Console.Error.WriteLine("Error: reference does not exist: " + referencePath);
                    return 1;
                }

                using (FileStream fs = File.OpenRead(referencePath))
                {
                    references.Add(MetadataReference.CreateFromStream(fs, filePath: referencePath));
                }
            }

            List<SyntaxTree> syntaxTrees = new List<SyntaxTree>();
            foreach (string sourcePath in compileItems)
            {
                string fullSourcePath = Path.Combine(Environment.CurrentDirectory, sourcePath);
                if (!File.Exists(fullSourcePath))
                {
                    Console.Error.WriteLine("Error: source file does not exist: " + fullSourcePath);
                    return 1;
                }

                using (FileStream fs = File.OpenRead(fullSourcePath))
                {
                    SourceText text = SourceText.From(fs);
                    syntaxTrees.Add(CSharpSyntaxTree.ParseText(text, path: fullSourcePath));
                }
            }

            Compilation compilation = CSharpCompilation.Create(
                "ShaderGen.App.GenerateShaders",
                syntaxTrees,
                references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            HlslBackend hlsl = new HlslBackend(compilation);
            Glsl330Backend glsl330 = new Glsl330Backend(compilation);
            GlslEs300Backend glsles300 = new GlslEs300Backend(compilation);
            Glsl450Backend glsl450 = new Glsl450Backend(compilation);
            MetalBackend metal = new MetalBackend(compilation);
            LanguageBackend[] languages = new LanguageBackend[]
            {
                hlsl,
                glsl330,
                glsles300,
                glsl450,
                metal,
            };

            List<IShaderSetProcessor> processors = new List<IShaderSetProcessor>();
            if (opt.ProcessorPath != null)
            {
                try
                {
                    Assembly assm = Assembly.LoadFrom(opt.ProcessorPath);
                    IEnumerable<Type> processorTypes = assm.GetTypes().Where(
                        t => t.GetInterface(nameof(ShaderGen) + "." + nameof(IShaderSetProcessor)) != null);
                    foreach (Type type in processorTypes)
                    {
                        IShaderSetProcessor processor = (IShaderSetProcessor)Activator.CreateInstance(type);
                        processor.UserArgs = opt.ProcessorArgs;
                        processors.Add(processor);
                    }
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    string msg = string.Join(Environment.NewLine, rtle.LoaderExceptions.Select(e => e.ToString()));
                    Console.WriteLine("FAIL: " + msg);
                    throw new Exception(msg);
                }
            }

            ShaderGenerator sg = new ShaderGenerator(compilation, languages, processors.ToArray());
            ShaderGenerationResult shaderGenResult;
            try
            {
                shaderGenResult = sg.GenerateShaders();
            }
            catch (Exception e) when (!Debugger.IsAttached)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("An error was encountered while generating shader code:");
                sb.AppendLine(e.ToString());
                Console.Error.WriteLine(sb.ToString());
                return -1;
            }

            Encoding outputEncoding = new UTF8Encoding(false);
            List<string> generatedFilePaths = new List<string>();
            foreach (LanguageBackend lang in languages)
            {
                string extension = BackendExtension(lang);
                IReadOnlyList<GeneratedShaderSet> sets = shaderGenResult.GetOutput(lang);
                foreach (GeneratedShaderSet set in sets)
                {
                    string name = set.Name;
                    if (set.VertexShaderCode != null)
                    {
                        string vsOutName = name + "-vertex." + extension;
                        string vsOutPath = Path.Combine(opt.OutputPath, vsOutName);
                        File.WriteAllText(vsOutPath, set.VertexShaderCode, outputEncoding);
                        bool succeeded = CompileCode(
                            lang,
                            vsOutPath,
                            set.VertexFunction.Name,
                            ShaderFunctionType.VertexEntryPoint,
                            out string[] genPaths,
                            opt.Debug);
                        if (succeeded)
                        {
                            generatedFilePaths.AddRange(genPaths);
                        }
                        if (!succeeded || opt.ListAllFiles)
                        {
                            generatedFilePaths.Add(vsOutPath);
                        }
                    }
                    if (set.FragmentShaderCode != null)
                    {
                        string fsOutName = name + "-fragment." + extension;
                        string fsOutPath = Path.Combine(opt.OutputPath, fsOutName);
                        File.WriteAllText(fsOutPath, set.FragmentShaderCode, outputEncoding);
                        bool succeeded = CompileCode(
                            lang,
                            fsOutPath,
                            set.FragmentFunction.Name,
                            ShaderFunctionType.FragmentEntryPoint,
                            out string[] genPaths,
                            opt.Debug);
                        if (succeeded)
                        {
                            generatedFilePaths.AddRange(genPaths);
                        }
                        if (!succeeded || opt.ListAllFiles)
                        {
                            generatedFilePaths.Add(fsOutPath);
                        }
                    }
                    if (set.ComputeShaderCode != null)
                    {
                        string csOutName = name + "-compute." + extension;
                        string csOutPath = Path.Combine(opt.OutputPath, csOutName);
                        File.WriteAllText(csOutPath, set.ComputeShaderCode, outputEncoding);
                        bool succeeded = CompileCode(
                            lang,
                            csOutPath,
                            set.ComputeFunction.Name,
                            ShaderFunctionType.ComputeEntryPoint,
                            out string[] genPaths,
                            opt.Debug);
                        if (succeeded)
                        {
                            generatedFilePaths.AddRange(genPaths);
                        }
                        if (!succeeded || opt.ListAllFiles)
                        {
                            generatedFilePaths.Add(csOutPath);
                        }
                    }
                }
            }

            File.WriteAllLines(opt.GenListFilePath, generatedFilePaths);

            return 0;
        }

        private static string NormalizePath(string path)
        {
            if (path == null)
            {
                return null;
            }
            else
            {
                return path.Trim();
            }
        }

        private static bool CompileCode(LanguageBackend lang, string shaderPath, string entryPoint, ShaderFunctionType type, out string[] paths, bool debug)
        {
            Type langType = lang.GetType();
            if (langType == typeof(HlslBackend))
            {
                bool result = CompileHlsl(shaderPath, entryPoint, type, out paths, debug);
                return result;
            }
            else if (langType == typeof(Glsl450Backend) && IsGlslangValidatorAvailable())
            {
                bool result = CompileSpirv(shaderPath, entryPoint, "main", type, out string path);
                paths = new[] { path };
                return result;
            }
            else if (langType == typeof(MetalBackend) && AreMetalMacOSToolsAvailable() && AreMetaliOSToolsAvailable())
            {
                bool macOSresult = CompileMetal(shaderPath, true, out string pathMacOS);
                bool iosResult = CompileMetal(shaderPath, false, out string pathiOS);
                paths = new[] { pathMacOS, pathiOS };
                return macOSresult && iosResult;
            }
            else
            {
                paths = Array.Empty<string>();
                return false;
            }
        }

        private static bool CompileHlsl(string shaderPath, string entryPoint, ShaderFunctionType type, out string[] paths, bool debug)
        {
            string pathDXBC = null;
            string pathDXIL = null;
            var result = CompileHlslByD3DCompiler(shaderPath, entryPoint, type, out pathDXBC, debug) 
                         && CompileHlslByDXC(shaderPath, entryPoint, type, out pathDXIL, debug);
            paths = new[] { pathDXBC, pathDXIL };
            return result;
        }

        private static bool CompileHlslByD3DCompiler(string shaderPath, string entryPoint, ShaderFunctionType type, out string path, bool debug)
        {
            try
            {
                var outputPath = shaderPath + ".dxbc";

                var profile = type switch {
                    ShaderFunctionType.VertexEntryPoint => "vs_5_0",
                    ShaderFunctionType.FragmentEntryPoint => "ps_5_0",
                    ShaderFunctionType.ComputeEntryPoint => "cs_5_0",
                };

                var shaderFlags = debug
                    ? ShaderFlags.SkipOptimization | ShaderFlags.Debug
                    : ShaderFlags.OptimizationLevel3;

                try
                {
                    var compilationResult = Compiler.CompileFromFile(shaderPath, entryPoint, profile, shaderFlags);

                    using var fileStream = File.OpenWrite(outputPath);
                    fileStream.Write(compilationResult.Span);
                    path = outputPath;
                    return true;
                }
                catch (SharpGenException e)
                {
                    Console.WriteLine($"Failed to compile HLSL: {e.Message}");
                }

            }
            catch (Win32Exception)
            {
                Console.WriteLine("Unable to invoke D3D compiler.");
            }

            path = null;
            return false;
        }

        private static bool CompileHlslByDXC(string shaderPath, string entryPoint, ShaderFunctionType type, out string path, bool debug)
        {
            try
            {
                var outputPath = shaderPath + ".dxil";

                var dxShaderStage = type switch {
                    ShaderFunctionType.VertexEntryPoint => DxcShaderStage.Vertex,
                    ShaderFunctionType.FragmentEntryPoint => DxcShaderStage.Pixel,
                    ShaderFunctionType.ComputeEntryPoint => DxcShaderStage.Compute,
                };

                DxcCompilerOptions dxOptions = debug
                    ? new() { SkipOptimizations = true, EnableDebugInfo = true }
                    : new() { OptimizationLevel = 3 };

                dxOptions.ShaderModel = DxcShaderModel.Model6_0;

                using var compilationResult = DxcCompiler.Compile(dxShaderStage, File.ReadAllText(shaderPath), entryPoint, dxOptions);

                if (compilationResult.GetStatus() != Result.Ok)
                {
                    Console.WriteLine($"Failed to compile HLSL: {compilationResult.GetErrors()}");
                }
                else
                {
                    using var fileStream = File.OpenWrite(outputPath);
                    fileStream.Write(compilationResult.GetObjectBytecode());
                    path = outputPath;
                    return true;
                }
            }
            catch (Win32Exception)
            {
                Console.WriteLine("Unable to invoke DXC.");
            }

            path = null;
            return false;
        }

        private static bool CompileSpirv(string shaderPath, string entryPoint, string sourceEntryPoint, ShaderFunctionType type, out string path)
        {
            string stage = type == ShaderFunctionType.VertexEntryPoint ? "vert"
                : type == ShaderFunctionType.FragmentEntryPoint ? "frag"
                : "comp";
            string outputPath = shaderPath + ".spv";
            string args = $"-V -S {stage} {shaderPath} -o {outputPath} -e {entryPoint} --source-entrypoint {sourceEntryPoint}";
            try
            {

                ProcessStartInfo psi = new ProcessStartInfo("glslangValidator", args);
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                Process p = Process.Start(psi);
                p.WaitForExit();

                if (p.ExitCode == 0)
                {
                    path = outputPath;
                    return true;
                }
                else
                {
                    throw new ShaderGenerationException(p.StandardOutput.ReadToEnd());
                }
            }
            catch (Win32Exception)
            {
                Console.WriteLine("Unable to launch glslangValidator tool.");
            }

            path = null;
            return false;
        }

        private static bool CompileMetal(string shaderPath, bool mac, out string path)
        {
            string metalPath = mac ? s_metalMacPath : s_metaliOSPath;
            string metallibPath = mac ? s_metallibMacPath : s_metallibiOSPath;

            string shaderPathWithoutExtension = Path.ChangeExtension(shaderPath, null);
            string extension = mac ? ".metallib" : ".ios.metallib";
            string outputPath = shaderPathWithoutExtension + extension;
            string bitcodePath = Path.GetTempFileName();
            string metalArgs = $"-c -o {bitcodePath} {shaderPath}";
            try
            {
                ProcessStartInfo metalPSI = new ProcessStartInfo(metalPath, metalArgs);
                metalPSI.RedirectStandardError = true;
                metalPSI.RedirectStandardOutput = true;
                Process metalProcess = Process.Start(metalPSI);
                metalProcess.WaitForExit();

                if (metalProcess.ExitCode != 0)
                {
                    throw new ShaderGenerationException(metalProcess.StandardError.ReadToEnd());
                }

                string metallibArgs = $"-o {outputPath} {bitcodePath}";
                ProcessStartInfo metallibPSI = new ProcessStartInfo(metallibPath, metallibArgs);
                metallibPSI.RedirectStandardError = true;
                metallibPSI.RedirectStandardOutput = true;
                Process metallibProcess = Process.Start(metallibPSI);
                metallibProcess.WaitForExit();

                if (metallibProcess.ExitCode != 0)
                {
                    throw new ShaderGenerationException(metallibProcess.StandardError.ReadToEnd());
                }

                path = outputPath;
                return true;
            }
            finally
            {
                File.Delete(bitcodePath);
            }
        }

        public static bool IsGlslangValidatorAvailable()
        {
            if (!s_glslangValidatorAvailable.HasValue)
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo("glslangValidator");
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    Process.Start(psi);
                    s_glslangValidatorAvailable = true;
                }
                catch { s_glslangValidatorAvailable = false; }
            }

            return s_glslangValidatorAvailable.Value;
        }

        public static bool AreMetalMacOSToolsAvailable()
        {
            if (!s_metalMacOSToolsAvailable.HasValue)
            {
                s_metalMacPath = FindXcodeTool("macosx", "metal");
                s_metallibMacPath = FindXcodeTool("macosx", "metallib");

                s_metalMacOSToolsAvailable = s_metalMacPath != null && s_metallibMacPath != null;
            }

            return s_metalMacOSToolsAvailable.Value;
        }

        public static bool AreMetaliOSToolsAvailable()
        {
            if (!s_metaliOSAvailable.HasValue)
            {
                s_metaliOSPath = FindXcodeTool("iphoneos", "metal");
                s_metallibiOSPath = FindXcodeTool("iphoneos", "metallib");

                s_metaliOSAvailable = s_metalMacPath != null && s_metallibMacPath != null;
            }

            return s_metaliOSAvailable.Value;
        }

        private static string FindXcodeTool(string sdk, string tool)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "xcrun",
                Arguments = $"-sdk {sdk} --find {tool}",
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            try
            {
                using (Process process = Process.Start(startInfo))
                using (StreamReader reader = process.StandardOutput)
                {
                    return reader.ReadLine();
                }
            }
            catch
            {
            }

            return null;
        }

        private static string BackendExtension(LanguageBackend lang)
        {
            if (lang.GetType() == typeof(HlslBackend))
            {
                return "hlsl";
            }
            else if (lang.GetType() == typeof(Glsl330Backend))
            {
                return "330.glsl";
            }
            else if (lang.GetType() == typeof(GlslEs300Backend))
            {
                return "300.glsles";
            }
            else if (lang.GetType() == typeof(Glsl450Backend))
            {
                return "450.glsl";
            }
            else if (lang.GetType() == typeof(MetalBackend))
            {
                return "metal";
            }

            throw new InvalidOperationException("Invalid backend type: " + lang.GetType().Name);
        }

        private static string GetXcodePlatformPath(string sdk)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "xcrun",
                    Arguments = $"-sdk {sdk} --show-sdk-platform-path",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                try
                {
                    using (Process process = Process.Start(startInfo))
                    using (StreamReader reader = process.StandardOutput)
                    {
                        return reader.ReadLine();
                    }
                }
                catch
                {
                }
            }
            return null;
        }
    }
}
