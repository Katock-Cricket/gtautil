using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Xml;
using Newtonsoft.Json.Linq;
using RageLib.GTA5.ResourceWrappers.PC.Meta;
using RageLib.Hash;
using RageLib.Resources.GTA5;
using RageLib.Resources.GTA5.PC.Meta;
using SharpDX;

namespace GTAUtil
{
    partial class Program
    {
        static void HandleFindOptions(string[] args)
        {
            CommandLine.Parse<FindOptions>(args, (opts, gOpts) =>
            {
                if (opts.Position == null || opts.Position.Count != 3)
                {
                    Console.Error.WriteLine("Please specify position with -p --position");
                    return;
                }

                Init(args);

                if (Cache == null)
                {
                    Console.Error.WriteLine("Please build cache first with buildcache");
                    return;
                }

                var position = new Vector3(opts.Position[0], opts.Position[1], opts.Position[2]);
                var filesToExtract = new List<string>();
                var c = CultureInfo.InvariantCulture;

                for (int i = 0; i < Cache["ymap"].Count; i++)
                {
                    var cYmap = Cache["ymap"][i];

                    var entitiesExtentsMin = new Vector3((float)cYmap["entitiesExtentsMin"]["x"], (float)cYmap["entitiesExtentsMin"]["y"], (float)cYmap["entitiesExtentsMin"]["z"]);
                    var entitiesExtentsMax = new Vector3((float)cYmap["entitiesExtentsMax"]["x"], (float)cYmap["entitiesExtentsMax"]["y"], (float)cYmap["entitiesExtentsMax"]["z"]);

                    if (Utils.BoxIntersectsSphere(entitiesExtentsMin, entitiesExtentsMax, position, opts.Radius))
                    {
                        string ymapFileName = ((string)cYmap["path"]).Split('\\').Last();
                        
                        Console.WriteLine("ymap: " + ymapFileName);

                        if(filesToExtract.IndexOf(ymapFileName) == -1)
                        {
                            filesToExtract.Add(ymapFileName);
                        }

                        for (int j = 0; j < cYmap["mloInstances"].Count; j++)
                        {
                            var cMloInstance = cYmap["mloInstances"][j];
                            var cMloInstanceHash = (uint)cMloInstance["name"];

                            var instancePos = new Vector3((float)cMloInstance["position"]["x"], (float)cMloInstance["position"]["y"], (float)cMloInstance["position"]["z"]);
                            var instanceRot = new Quaternion((float)cMloInstance["rotation"]["x"], (float)cMloInstance["rotation"]["y"], (float)cMloInstance["rotation"]["z"], (float)cMloInstance["rotation"]["w"]);

                            for (int k = 0; k < Cache["ytyp"].Count; k++)
                            {
                                var cYtyp = Cache["ytyp"][k];
                                var cYtypHash = (uint)cYtyp["hash"];

                                for (int l = 0; l < cYtyp["mloArchetypes"].Count; l++)
                                {
                                    var cMloArch = cYtyp["mloArchetypes"][l];
                                    var cMloArchHash = (uint)cMloArch["name"];

                                    if (cMloInstanceHash == cMloArchHash)
                                    {
                                        string ytypFileName = ((string)cYtyp["path"]).Split('\\').Last();

                                        Console.WriteLine("  ytyp => " + ytypFileName);
                                        Console.WriteLine("    mlo => " + Jenkins.GetString(cMloArchHash));
                                        Console.WriteLine("    position => " + instancePos.X.ToString(c) + "," + instancePos.Y.ToString(c) + "," + instancePos.Z.ToString(c));
                                        Console.WriteLine("    rotation => " + instanceRot.X.ToString(c) + "," + instanceRot.Y.ToString(c) + "," + instanceRot.Z.ToString(c) + "," + instanceRot.W.ToString(c));

                                        if (filesToExtract.IndexOf(ytypFileName) == -1)
                                        {
                                            filesToExtract.Add(ytypFileName);
                                        }

                                        for (int m = 0; m < cMloArch["rooms"].Count; m++)
                                        {
                                            var cMloRoom = cMloArch["rooms"][m];

                                            var roomBbMin = new Vector3((float)cMloRoom["bbMin"]["x"], (float)cMloRoom["bbMin"]["y"], (float)cMloRoom["bbMin"]["z"]);
                                            var roomBbMax = new Vector3((float)cMloRoom["bbMax"]["x"], (float)cMloRoom["bbMax"]["y"], (float)cMloRoom["bbMax"]["z"]);

                                            var roomBbMinWorld = instancePos + roomBbMin;
                                            var roomBbMaxWorld = instancePos + roomBbMax;

                                            roomBbMinWorld = Utils.RotateTransform(Quaternion.Conjugate(instanceRot), roomBbMinWorld, Vector3.Zero);
                                            roomBbMaxWorld = Utils.RotateTransform(Quaternion.Conjugate(instanceRot), roomBbMaxWorld, Vector3.Zero);

                                            if (Utils.BoxIntersectsSphere(roomBbMinWorld, roomBbMaxWorld, position, opts.Radius))
                                            {
                                                Console.WriteLine("      room => " + cMloRoom["name"]);
                                            }
                                        }

                                    }

                                }
                            }
                        }

                        Console.WriteLine("");
                    }
                }

                bool matchEnd(string name, out string baseName)
                {
                    baseName = null;
                    var spl = new List<string>(name.Split('_'));
                    spl.RemoveAt(0);
                    string maybeBase = string.Join("_", spl);

                    foreach (string f in filesToExtract)
                    {
                        if(f == maybeBase)
                        {
                            baseName = maybeBase;
                            return true;
                        }
                    }

                    return false;
                }

                var latest = new Dictionary<string, Tuple<int, string>>();
                var kept = new Dictionary<string, string>();

                RageLib.GTA5.Utilities.ArchiveUtilities.ForEachResourceFile(Settings.Default.GTAFolder, (fullFileName, file, encryption) =>
                {
                    if(!(fullFileName.EndsWith(".ymap") || fullFileName.EndsWith(".ytyp")))
                    {
                        return;
                    }

                    string fileName = fullFileName.Split('\\').Last();

                    if (matchEnd(fileName, out string baseName))
                    {
                        int dlcLevel = GetDLCLevel(fullFileName);

                        if (latest.TryGetValue(baseName, out Tuple<int, string> entry))
                        {
                            if (dlcLevel > entry.Item1)
                            {
                                latest[baseName] = new Tuple<int, string>(dlcLevel, fullFileName);
                            }
                        }
                        else
                        {
                            latest[baseName] = new Tuple<int, string>(dlcLevel, fullFileName);
                        }
                    }
                });

                RageLib.GTA5.Utilities.ArchiveUtilities.ForEachResourceFile(Settings.Default.GTAFolder, (fullFileName, file, encryption) =>
                {
                    if (!(fullFileName.EndsWith(".ymap") || fullFileName.EndsWith(".ytyp")))
                    {
                        return;
                    }

                    foreach (var entry in latest)
                    {
                        if(fullFileName == entry.Value.Item2)
                        {
                            string fileName = fullFileName.Split('\\').Last();
                            file.Export(opts.OutputDirectory + "\\" + fileName);
                        }
                    }
                });

            });
        }
    }
}
