using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace AssetPipeline {
    static class Program {

        private class Dirs {
            public string source;      // Where to read shape files
            public string working;     // Where to store temp files
            public string unityAssets; // Unity's asset dir
            public string dropbox;     // Shared dir
        }

        static void Main(string[] args) {
            if (args.Length != 4) {
                LogError("Invalid number of arguments. AtlasCreator expects the following arguments:\n"+
                    "1) Source directory. AtlasCreator will search through the source directory for\n"+
                    "   sub directories containing PNG-files. Each sub directory will be turned into\n"+
                    "   an atlas.\n"+
                    "2) Working directory. Where to store temp files.\n"+
                    "3) Unity Asset directory. The generated atlas will be copied to the Asset directory.\n"+
                    "4) Dropbox directory. The generated atlas and obj.files will be copied to Dropbox.\n");
                return;
            }

            Dirs dirs = new Dirs {
                source      = ParseArgAsDir(args[0]),
                working     = ParseArgAsDir(args[1]),
                unityAssets = ParseArgAsDir(args[2]),
                dropbox     = ParseArgAsDir(args[3]),
            };

            LogHeader("Directories");
            Log($"      source: {dirs.source}\n"+
                $"     working: {dirs.working}\n"+
                $"Unity assets: {dirs.unityAssets}\n"+
                $"     Dropbox: {dirs.dropbox}\n");

            LogHeader("Atlases");
            Directory
               .GetDirectories(dirs.source)
               .ToList()
               .ForEach(atlasDir => {

                    // Generate atlas
                    string atlasName         = atlasDir.Substring(atlasDir.LastIndexOf('/') + 1);
                    string atlasImgFilename  = $"{atlasName}_atlas.png";
                    string atlasDataFilename = $"{atlasName}_atlas.json";
                    string atlasWorkingPath  = $"{dirs.working}/{atlasName}";
                    string atlasImgPath      = $"{atlasWorkingPath}/{atlasImgFilename}";
                    string atlasDataPath     = $"{atlasWorkingPath}/{atlasDataFilename}";

                    Log($"         Atlas name: {atlasName}\n"+
                        $"          Atlas dir: {atlasDir}\n"+
                        $" Atlas img filename: {atlasImgFilename}\n"+
                        $"     Atlas img path: {atlasImgPath}\n"+
                        $"Atlas data filename: {atlasDataFilename}\n"+
                        $"    Atlas data path: {atlasDataPath}\n"+
                        $" Atlas working path: {atlasWorkingPath}\n");

                    CreateDirectory(atlasWorkingPath);

                    string tpArgs = $"--sheet {atlasImgPath} " +
                                    $"--data {atlasDataPath} " +
                                    "--texture-format png " +
                                    "--format json " +
                                    "--trim-sprite-names " +
                                    "--algorithm Polygon " +
                                    "--align-to-grid 1 " +
                                    "--max-size 2048 " +
                                    "--size-constraints POT " +
                                    "--force-squared " +
                                    "--shape-padding 0 " +
                                    "--border-padding 0 " +
                                    "--enable-rotation " +
                                    "--trim-mode Polygon " +
                                    "--trim-margin 1 " +
                                    "--tracer-tolerance 200 " +
                                    "--opt RGBA8888 " +
                                    "--alpha-handling ClearTransparentPixels " +
                                    "--png-opt-level 0 " +
                                    $"{atlasDir}/Shapes";

                    LogHeader("Texture Packer");
                    ExecuteCommand("TexturePacker", tpArgs);

                    // Generate and save obj-files
                    string objTargetDir = Path.Combine(atlasDir, "OBJs");
                    Log($"OBJ dir: {objTargetDir}");
                    CreateDirectory(objTargetDir);

                    string[] atlasLines = File.ReadAllLines(atlasDataPath);
                    Mesh[] meshes = OBJCreator.CreateMeshes(atlasLines);
                    string[] objs = OBJCreator.CreateOBJs(meshes);
                    Log($"Generated {meshes.Length} meshes");

                    string[] objFilenames = meshes.Select(mesh => $"{mesh.name}.obj").ToArray();

                    DeleteFiles(Directory
                               .GetFiles(objTargetDir)
                               .Where(filePath => Array.IndexOf(objFilenames, Path.GetFileName(filePath)) < 0)
                               .ToArray());

                    WriteFiles(objs, objFilenames.Select(filename => Path.Combine(objTargetDir, filename)).ToArray());

                    // Copy atlas to unity assets
                    LogHeader("Copy atlas to Unity Assets");
                    CopyFile(atlasImgPath, Path.Combine(dirs.unityAssets, atlasImgFilename));

                    // Copy atlas and obj-files to Dropbox
                    LogHeader("Copy atlas and OBJ-files to Dropbox");
                    CopyFile(atlasImgPath, Path.Combine(dirs.dropbox, atlasImgFilename));

                    string dropboxOBJs = Path.Combine(dirs.dropbox, $"{atlasName}_OBJ");
                    CreateDirectory(dropboxOBJs);

                    DeleteFiles(Directory
                               .GetFiles(dropboxOBJs)
                               .Where(filePath => Array.IndexOf(objFilenames, Path.GetFileName(filePath)) < 0)
                               .ToArray());

                    objFilenames.ToList().ForEach(objfile => {
                        CopyFile(Path.Combine(objTargetDir, objfile),
                                 Path.Combine(dropboxOBJs, objfile));
                    });
                });
        }

        private static void DeleteFiles(string[] filePaths) {
            foreach (string filePath in filePaths) {
                LogError($"Delete file: {filePath}");
                File.Delete(filePath);
            }
        }

        private static void CopyFile(string fromPath, string toPath) {
                Log($"Copy from: {fromPath}\n"+
                    $"       to: {toPath}");
                File.Copy(fromPath, toPath, true);
        }

        private static void WriteFiles(string[] contents, string[] filenames) {
            for (int i=0; i<contents.Length; ++i) {
                Log($"Write file: {filenames[i]}");
                using (StreamWriter sw = new StreamWriter(filenames[i])) {
                    sw.Write(contents[i]);
                }
            }
        }

        private static void ExecuteCommand(string command, string args) {
            Process process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = command,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UserName = Environment.UserName
                }
            };

            StringBuilder stdOut = new StringBuilder();
            process.OutputDataReceived += (sender, outArgs) => stdOut.AppendLine(outArgs.Data);

            string stdError = "";
            try {
                process.Start();
                process.BeginOutputReadLine();
                stdError = process.StandardError.ReadToEnd();
                process.WaitForExit();
            }
            catch (Exception e) {
                Log($"ERROR while executing {process.StartInfo.FileName} with arguments:\n"
                  + $"{args}\n"
                  + $"{e.Message}");
                return;
            }

            if (process.ExitCode == 0) {
                Log(stdOut.ToString());
                return;
            }

            StringBuilder message = new StringBuilder();

            if (!string.IsNullOrEmpty(stdError)) {
                message.AppendLine($"{stdError}");
            }

            if (stdOut.Length == 0) {
                return;
            }

            message.AppendLine(stdOut.ToString());
            Log(message.ToString());
        }

        private static void CreateDirectory(string path) {
            if (Directory.Exists(path)) {
                return;
            }
            Log($"Create directory: {path}");
            Directory.CreateDirectory(path);
        }

        private static void LogHeader(string str) {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(str);
            Console.ForegroundColor = ConsoleColor.White;
        }

        private static void Log(string str) {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(str);
        }

        private static void LogError(string str) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(str);
            Console.ForegroundColor = ConsoleColor.White;
        }

        private static string ParseArgAsDir(string path) {
            path = path == "." ? Directory.GetCurrentDirectory() : path;
            return path.EndsWith('/') ? path.Substring(0, path.Length - 1) : path;
        }
    }
}
