
using System;
using System.Collections.Generic;
using System.IO;
using SharpDX;
using RageLib.GTA5.Utilities;
using RageLib.Resources.GTA5.PC.GameFiles;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.Hash;
using RageLib.Resources.GTA5.PC.Meta;
using System.Runtime.CompilerServices;

namespace GTAUtil
{
    partial class Program
    {
        static void HandlePrefixYmapOptions(string[] args)
        {
            CommandLine.Parse<PrefixYmapOptions>(args, (opts, gOpts) =>
            {
                if (opts.OutputDirectory == null)
                {
                    Console.Error.WriteLine("Please provide output directory with --output");
                    return;
                }

                Init(args);

                if (!Directory.Exists(opts.OutputDirectory))
                    Directory.CreateDirectory(opts.OutputDirectory);

                foreach (var info in Utils.Expand(opts.InputFiles))
                {
                    if (info.Extension.EndsWith(".ymap"))
                    {
                        Jenkins.Ensure(info.Name.Replace(".ymap", "").ToLower());
                    }
                }

                foreach (var info in Utils.Expand(opts.InputFiles))
                {
                    if (info.Extension.EndsWith(".ymap"))
                    {
                        Jenkins.Ensure(info.Name.Replace(".ymap", "").ToLower());
                        var ymap = new YmapFile();
                        ymap.Load(info.FullName);

                        string ymapName = Jenkins.GetString((uint)ymap.CMapData.Name);
                        string parentName = Jenkins.GetString((uint)ymap.CMapData.Parent);

                        if (!ymapName.StartsWith(opts.Prefix))
                        {
                            foreach(string replace in opts.Replace)
                            {
                                if (ymapName.StartsWith(replace))
                                {
                                    ymapName = opts.Prefix + ymapName.Remove(0, replace.Length);
                                }

                                if (parentName.StartsWith(replace))
                                {
                                    parentName = opts.Prefix + parentName.Remove(0, replace.Length);
                                };
                            }
                        }

                        ymap.CMapData.Name = (MetaName)Jenkins.Hash(ymapName);
                        ymap.CMapData.Parent = (MetaName)Jenkins.Hash(parentName);
                        ymap.CMapData.Block.Name = ymapName;

                        Console.WriteLine(ymapName);

                        ymap.Save(opts.OutputDirectory + "\\" + ymapName + ".ymap");
                    };
                };

            });
        }
    }
}
