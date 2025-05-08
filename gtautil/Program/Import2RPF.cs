using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using RageLib.Archives;
using RageLib.Data;
using RageLib.GTA5.Archives;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Resources.PC;
using RageLib.Resources.GTA5;

namespace GTAUtil
{
    partial class Program
    {
        static void HandleImport2RPFOptions(string[] args)
        {
            CommandLine.Parse<Import2RPFOptions>(args, (opts, gOpts) =>
            {
                EnsureKeys();

                // Validate parameters
                if (string.IsNullOrWhiteSpace(opts.InputPath))
                {
                    Console.WriteLine("Please provide input path with -i --input");
                    return;
                }

                if (string.IsNullOrWhiteSpace(opts.OutputRpf))
                {
                    Console.WriteLine("Please provide output RPF with -o --output");
                    return;
                }

                bool isDirectory = Directory.Exists(opts.InputPath);
                bool isFile = File.Exists(opts.InputPath);

                if (!isDirectory && !isFile)
                {
                    Console.WriteLine($"Input path not found: {opts.InputPath}");
                    return;
                }

                try
                {
                    using (var fs = new FileStream(opts.OutputRpf, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                    {
                        RageArchiveWrapper7 rpf = fs.Length > 0
                            ? RageArchiveWrapper7.Open(fs, Path.GetFileName(opts.OutputRpf))
                            : RageArchiveWrapper7.Create(fs, Path.GetFileName(opts.OutputRpf));

                        rpf.archive_.Encryption = RageArchiveEncryption7.NG;

                        try
                        {
                            if (isDirectory)
                            {
                                ProcessDirectoryImport(rpf, opts.InputPath, opts.InternalPath);
                            }
                            else
                            {
                                ProcessSingleFileImport(rpf, opts.InputPath, opts.InternalPath);
                            }

                            rpf.Flush();
                            Console.WriteLine("Import completed successfully");
                        }
                        finally
                        {
                            rpf.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                    throw;
                }
            });
        }

        private static void ProcessDirectoryImport(RageArchiveWrapper7 rpf, string sourceDir, string baseArchivePath)
        {
            string basePath = Path.GetFullPath(sourceDir).TrimEnd('\\') + "\\";
            var allFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);

            foreach (var file in allFiles)
            {
                string relativePath = Path.GetFullPath(file).Substring(basePath.Length);
                string targetPath = baseArchivePath != null
                    ? Path.Combine(baseArchivePath, relativePath)
                    : relativePath;

                targetPath = targetPath.Replace('/', '\\').Trim('\\');
                ImportFileToArchive(rpf, file, targetPath);
            }
        }

        private static void ProcessSingleFileImport(RageArchiveWrapper7 rpf, string sourceFile, string targetArchivePath)
        {
            string fileName = Path.GetFileName(sourceFile);
            string targetPath = (targetArchivePath ?? fileName).Replace('/', '\\').Trim('\\');
            ImportFileToArchive(rpf, sourceFile, targetPath);
        }

        private static void ImportFileToArchive(RageArchiveWrapper7 archive, string sourceFilePath, string targetArchivePath)
        {
            string[] pathParts = targetArchivePath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            IArchiveDirectory currentDir = archive.Root;

            for (int i = 0; i < pathParts.Length - 1; i++)
            {
                string part = pathParts[i];

                if (part.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                {
                    currentDir = HandleNestedRpf(currentDir, part, pathParts, i, sourceFilePath);
                    return;
                }

                currentDir = GetOrCreateDirectory(currentDir, part);
            }

            ImportFinalFile(currentDir, pathParts[pathParts.Length - 1], sourceFilePath, targetArchivePath);
        }

        private static IArchiveDirectory HandleNestedRpf(IArchiveDirectory parentDir, string rpfName, string[] pathParts, int currentIndex, string sourcePath)
        {
            var existingRpfFile = parentDir.GetFile(rpfName) as IArchiveBinaryFile;

            using (var tempStream = new MemoryStream())
            {
                RageArchiveWrapper7 nestedRpf;

                if (existingRpfFile != null)
                {
                    existingRpfFile.Export(tempStream);
                    tempStream.Position = 0;
                    nestedRpf = RageArchiveWrapper7.Open(tempStream, rpfName);
                    parentDir.DeleteFile(existingRpfFile);
                    Console.WriteLine($"Updating nested RPF: {rpfName}");
                }
                else
                {
                    nestedRpf = RageArchiveWrapper7.Create(tempStream, rpfName);
                    nestedRpf.archive_.Encryption = RageArchiveEncryption7.NG;
                    Console.WriteLine($"Creating new nested RPF: {rpfName}");
                }

                try
                {
                    IArchiveDirectory currentDir = nestedRpf.Root;

                    // Process remaining path parts inside the RPF
                    for (int i = currentIndex + 1; i < pathParts.Length - 1; i++)
                    {
                        currentDir = GetOrCreateDirectory(currentDir, pathParts[i]);
                    }

                    ImportFinalFile(currentDir, pathParts[pathParts.Length - 1],
                        sourcePath, string.Join("\\", pathParts.Skip(currentIndex + 1)));

                    nestedRpf.Flush();
                    tempStream.Position = 0;

                    var newBinaryFile = parentDir.CreateBinaryFile();
                    newBinaryFile.Name = rpfName;
                    newBinaryFile.Import(tempStream);

                    return parentDir;
                }
                finally
                {
                    nestedRpf.Dispose();
                }
            }
        }

        private static IArchiveDirectory GetOrCreateDirectory(IArchiveDirectory parent, string dirName)
        {
            var dir = parent.GetDirectory(dirName);
            if (dir == null)
            {
                dir = parent.CreateDirectory();
                dir.Name = dirName;
            }
            return dir;
        }

        private static void ImportFinalFile(IArchiveDirectory targetDir, string fileName, string sourcePath, string fullArchivePath)
        {
            var existingFile = targetDir.GetFile(fileName);
            if (existingFile != null)
            {
                targetDir.DeleteFile(existingFile);
                Console.WriteLine($"Replacing: {fullArchivePath}");
            }

            bool isResource = false;
            foreach (var type in ResourceFileTypes_GTA5_pc.AllTypes)
            {
                if (fileName.EndsWith(type.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    if (type == ResourceFileTypes_GTA5_pc.Meta)
                    {
                        HandleMetaFile(targetDir, fileName, sourcePath, ref isResource);
                    }
                    else
                    {
                        ImportResourceFile(targetDir, fileName, sourcePath);
                        isResource = true;
                    }
                    break;
                }
            }

            if (!isResource)
            {
                ImportBinaryFile(targetDir, fileName, sourcePath);
            }

            Console.WriteLine($"Imported: {fullArchivePath}");
        }

        private static void HandleMetaFile(IArchiveDirectory dir, string fileName, string filePath, ref bool isResource)
        {
            using (var fs = new FileStream(filePath, FileMode.Open))
            {
                var reader = new DataReader(fs, Endianess.BigEndian);
                var ident = reader.ReadUInt32();
                fs.Position = 0;

                if (ident == 0x5053494E) // 'PSIN'
                {
                    ImportBinaryFile(dir, fileName, fs);
                }
                else
                {
                    ImportResourceFile(dir, fileName, fs);
                    isResource = true;
                }
            }
        }

        private static void ImportResourceFile(IArchiveDirectory dir, string fileName, object source)
        {
            var resource = dir.CreateResourceFile();
            resource.Name = fileName;

            if (source is string path)
                resource.Import(path);
            else if (source is Stream stream)
                resource.Import(stream);
        }

        private static void ImportBinaryFile(IArchiveDirectory dir, string fileName, object source)
        {
            var binFile = dir.CreateBinaryFile();
            binFile.Name = fileName;

            if (source is string path)
                binFile.Import(path);
            else if (source is Stream stream)
                binFile.Import(stream);
        }
    }
}