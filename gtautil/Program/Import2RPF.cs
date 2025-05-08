using System;
using System.IO;
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
                //EnsurePath();
                EnsureKeys();

                // 参数验证
                if (string.IsNullOrWhiteSpace(opts.InputFile))
                {
                    Console.WriteLine("Please provide input file with -i --input");
                    return;
                }

                if (string.IsNullOrWhiteSpace(opts.OutputRpf))
                {
                    Console.WriteLine("Please provide output RPF with -o --output");
                    return;
                }

                if (!File.Exists(opts.InputFile))
                {
                    Console.WriteLine($"Input file not found: {opts.InputFile}");
                    return;
                }

                var fileInfo = new FileInfo(opts.InputFile);
                string targetPath = (opts.InternalPath ?? fileInfo.Name).Replace('/', '\\').Trim('\\');

                // 递归导入函数
                void ImportToArchive(RageArchiveWrapper7 archive, string[] pathParts, int currentIndex)
                {
                    IArchiveDirectory currentDir = archive.Root;

                    // 处理路径组件（除最后一个外）
                    for (int i = currentIndex; i < pathParts.Length - 1; i++)
                    {
                        string part = pathParts[i];

                        // 处理嵌套RPF
                        if (part.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                        {
                            HandleNestedRpf(ref currentDir, part, pathParts, i);
                            return;
                        }

                        // 处理普通目录
                        currentDir = GetOrCreateDirectory(currentDir, part);
                    }

                    // 处理最终文件
                    ImportFinalFile(currentDir, pathParts[pathParts.Length - 1], opts.InputFile);
                }

                // 处理嵌套RPF逻辑
                void HandleNestedRpf(ref IArchiveDirectory currentDir, string rpfName, string[] pathParts, int currentIndex)
                {
                    // 删除已存在的RPF文件（如果存在且需要替换）
                    var existingRpfFile = currentDir.GetFile(rpfName) as IArchiveBinaryFile;
                    if (existingRpfFile != null)
                    {
                        currentDir.DeleteFile(existingRpfFile);
                        Console.WriteLine($"Replaced nested RPF: {rpfName}");
                    }

                    // 创建或打开嵌套RPF
                    using (var tempStream = new MemoryStream())
                    {
                        RageArchiveWrapper7 nestedRpf;

                        if (existingRpfFile != null)
                        {
                            // 打开现有RPF
                            existingRpfFile.Export(tempStream);
                            tempStream.Position = 0;
                            nestedRpf = RageArchiveWrapper7.Open(tempStream, rpfName);
                        }
                        else
                        {
                            // 创建新RPF
                            nestedRpf = RageArchiveWrapper7.Create(tempStream, rpfName);
                            nestedRpf.archive_.Encryption = RageArchiveEncryption7.NG;
                        }

                        try
                        {
                            // 递归处理剩余路径
                            ImportToArchive(nestedRpf, pathParts, currentIndex + 1);

                            // 将修改后的RPF写回父级
                            nestedRpf.Flush();
                            tempStream.Position = 0;

                            var newBinaryFile = currentDir.CreateBinaryFile();
                            newBinaryFile.Name = rpfName;
                            newBinaryFile.Import(tempStream);
                        }
                        finally
                        {
                            nestedRpf.Dispose();
                        }
                    }
                }

                // 获取或创建目录
                IArchiveDirectory GetOrCreateDirectory(IArchiveDirectory parent, string dirName)
                {
                    var dir = parent.GetDirectory(dirName);
                    if (dir == null)
                    {
                        dir = parent.CreateDirectory();
                        dir.Name = dirName;
                    }
                    return dir;
                }

                // 导入最终文件
                void ImportFinalFile(IArchiveDirectory targetDir, string fileName, string sourcePath)
                {
                    // 删除已存在的文件（如果存在）
                    var existingFile = targetDir.GetFile(fileName);
                    if (existingFile != null)
                    {
                        targetDir.DeleteFile(existingFile);
                        Console.WriteLine($"Replaced existing file: {fileName}");
                    }

                    // 根据文件类型处理导入
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

                    //Console.WriteLine($"Imported: {string.Join("\\", pathParts)}");
                }

                // 特殊处理meta文件
                void HandleMetaFile(IArchiveDirectory dir, string fileName, string filePath, ref bool isResource)
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

                // 导入资源文件（泛型方法）
                void ImportResourceFile(IArchiveDirectory dir, string fileName, object source)
                {
                    var resource = dir.CreateResourceFile();
                    resource.Name = fileName;

                    if (source is string path)
                        resource.Import(path);
                    else if (source is Stream stream)
                        resource.Import(stream);
                }

                // 导入二进制文件（泛型方法）
                void ImportBinaryFile(IArchiveDirectory dir, string fileName, object source)
                {
                    var binFile = dir.CreateBinaryFile();
                    binFile.Name = fileName;

                    if (source is string path)
                        binFile.Import(path);
                    else if (source is Stream stream)
                        binFile.Import(stream);
                }

                // 主执行逻辑
                try
                {
                    string[] pathParts = targetPath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);

                    using (var fs = new FileStream(opts.OutputRpf, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                    {
                        RageArchiveWrapper7 rpf = fs.Length > 0
                            ? RageArchiveWrapper7.Open(fs, Path.GetFileName(opts.OutputRpf))
                            : RageArchiveWrapper7.Create(fs, Path.GetFileName(opts.OutputRpf));

                        rpf.archive_.Encryption = RageArchiveEncryption7.NG;

                        try
                        {
                            ImportToArchive(rpf, pathParts, 0);
                            rpf.Flush();
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
    }
}