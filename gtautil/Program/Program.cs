﻿using Newtonsoft.Json.Linq;
using RageLib.Archives;
using RageLib.GTA5.Archives;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Cryptography;
using RageLib.GTA5.Cryptography.Helpers;
using RageLib.GTA5.ResourceWrappers.PC.Meta.Structures;
using RageLib.GTA5.Utilities;
using RageLib.Hash;
using RageLib.Resources.GTA5.PC.Drawables;
using RageLib.Resources.GTA5.PC.GameFiles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace GTAUtil
{

    partial class Program
    {
        static dynamic Cache = null;
        static string[] DLCList;
        static List<RpfFileEntry> Files = new List<RpfFileEntry>(); // All files
        static Dictionary<string, RpfFileEntry> DLCFiles = new Dictionary<string, RpfFileEntry>();  // Latest dlc version of each file
        static Dictionary<string, Dictionary<uint, RpfFileEntry>> FileHashes = new Dictionary<string, Dictionary<uint, RpfFileEntry>>();    // Files by extension then hash
        static Dictionary<uint, Drawable> DrawableCache = new Dictionary<uint, Drawable>();
        static Dictionary<string, YmtPedDefinitionFile> PedYmtCache = new Dictionary<string, YmtPedDefinitionFile>();
        static Dictionary<uint, MCBaseArchetypeDef> ArchetypeCache = new Dictionary<uint, MCBaseArchetypeDef>();

        static string AssemblyDirectory
        {
            get
            {
                string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                UriBuilder uri = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(uri.Path);
                return Path.GetDirectoryName(path);
            }
        }

        static void Main(string[] args)
        {
            HandleTestOptions(args);
            HandleCreateArchiveOptions(args);
            HandleExtractArchiveOptions(args);
            HandleFixArchiveOptions(args);
            HandleBuildCacheOptions(args);
            HandleCompileDrawableShadersOptions(args);
            HandleCompileGxt2Options(args);
            HandleDeamonOptions(args);
            HandleGenColOptions(args);
            HandleExportMetaOptions(args);
            HandleExtractEntitiesOptions(args);
            HandleFindOptions(args);
            HandleFindPropsOptions(args);
            HandleGenLODLightsOptions(args);
            HandleGenPedDefsOptions(args);
            HandleGenPropDefsOptions(args);
            HandleGetDLCListOptions(args);
            HandleImportMetaOptions(args);
            HandleInjectEntitiesOptions(args);
            HandleMergeYmapOptionsOptions(args);
            HandleMoveYmapOptionsOptions(args);
            HandleWorldToMLOOptions(args);
            HandlePrefixYmapOptions(args);
            // HandlYmapToYdrOptions(args);

            if (args.Length == 0 || args[0] == "help")
            {
                Console.Error.Write(CommandLine.GenHelp());
            }
        }

        public static void Init(string[] args)
        {
            EnsurePath();
            EnsureKeys();
            EnsureCache();

            DLCList = GetDLCList();

            HandleGenericOptions(args);
        }

        public static void EnsurePath()
        {
            if (!File.Exists(Settings.Default.GTAFolder + "\\GTA5.exe"))
            {
                string path = Settings.Default.GTAFolder;

                while (!File.Exists(path + "\\GTA5.exe"))
                {
                    Console.Error.Write("GTAV folder : ");

                    path = Console.ReadLine();

                }

                Settings.Default.GTAFolder = path;

                Settings.Default.Save();
            }
        }

        public static void EnsureKeys()
        {
            GTA5Constants.PC_AES_KEY = Resource.gtav_aes_key;
            GTA5Constants.PC_NG_KEYS = CryptoIO.ReadNgKeys(Resource.gtav_ng_key);
            GTA5Constants.PC_NG_DECRYPT_TABLES = CryptoIO.ReadNgTables(Resource.gtav_ng_decrypt_tables);
            GTA5Constants.PC_NG_ENCRYPT_TABLES = CryptoIO.ReadNgTables(Resource.gtav_ng_encrypt_tables);
            GTA5Constants.PC_NG_ENCRYPT_LUTs = CryptoIO.ReadNgLuts(Resource.gtav_ng_encrypt_luts);
            GTA5Constants.PC_LUT = Resource.gtav_hash_lut;
        }

        public static void EnsureCache()
        {
            Console.Error.WriteLine("Loading cache");

            if (File.Exists(AssemblyDirectory + "\\strings.txt"))
            {
                using (StreamReader reader = new StreamReader(AssemblyDirectory + "\\strings.txt"))
                {
                    string line;

                    while((line = reader.ReadLine()) != null)
                    {
                        Utils.Hash(line);
                    }
                }
            }

            if (File.Exists(AssemblyDirectory + "\\cache.json"))
            {
                Cache = JObject.Parse(File.ReadAllText(AssemblyDirectory + "\\cache.json"));
            }
        }

        public static void EnsureFiles(int targetDlcLevel)
        {
            Console.Error.WriteLine("Loading file tree");

            ArchiveUtilities.ForEachFile(Settings.Default.GTAFolder, (string fullFileName, IArchiveFile file, RageArchiveEncryption7 encryption) =>
            {
                var dlcLevel = GetDLCLevel(fullFileName);

                if (dlcLevel > targetDlcLevel)
                    return;

                string[] path = fullFileName.Split('\\');
                string[] split = path[path.Length - 1].ToLowerInvariant().Split('.');
                string name = split[0];
                string ext = split[split.Length - 1];
                uint hash = Jenkins.Hash(name);

                var rpfEntry = new RpfFileEntry()
                {
                    Hash = hash,
                    Name = fullFileName.Replace('/', '\\').ToLowerInvariant().Split('\\').Last(),
                    FullFileName = fullFileName,
                    DlcLevel = dlcLevel,
                    Ext = ext,
                };

                if(DLCFiles.ContainsKey(rpfEntry.Name))
                {

                    if (rpfEntry.DlcLevel > DLCFiles[rpfEntry.Name].DlcLevel)
                        DLCFiles[rpfEntry.Name] = rpfEntry;
                }
                else
                {
                    DLCFiles[rpfEntry.Name] = rpfEntry;
                }

                Files.Add(rpfEntry);

                Dictionary<uint, RpfFileEntry> dict;

                if (!(FileHashes.TryGetValue(ext, out dict)))
                {
                    dict = new Dictionary<uint, RpfFileEntry>();
                }

                dict[hash] = rpfEntry;

                FileHashes[ext] = dict;

            });
        }

        public static void EnsureArchetypes(int targetDlcLevel)
        {
            Console.Error.WriteLine("Loading archetypes");

            ArchiveUtilities.ForEachResourceFile(Settings.Default.GTAFolder, (string fullFileName, IArchiveResourceFile file, RageArchiveEncryption7 encryption) =>
            {
                var dlcLevel = GetDLCLevel(fullFileName);

                if (dlcLevel > targetDlcLevel)
                    return;

                if (file.Name.EndsWith(".ytyp"))
                {
                    var ytyp = new YtypFile();

                    using (var ms = new MemoryStream())
                    {
                        file.Export(ms);

                        ytyp.Load(ms);
                    }

                    for(int i=0; i< ytyp.CMapTypes.Archetypes.Count; i++)
                    {
                        var archetype = ytyp.CMapTypes.Archetypes[i];

                        ArchetypeCache[(uint) archetype.Name] = archetype;
                    }
                   
                }
            });
        }

        public static string[] GetDLCList()
        {
            Console.Error.WriteLine("Loading DLC list");

            var orderRegex    = new Regex("<order value=\"(\\d*)\"");
            var minOrderRegex = new Regex("<minOrder value=\"(\\d*)\"");
            var pathRegex     = new Regex(@"\\dlcpacks\\([a-z0-9_]*)\\");
            var pathRegex2    = new Regex(@"\\dlc_patch\\([a-z0-9_]*)\\");

            var dlclist = new List<string>();
            var dlcOrders = new Dictionary<string, Tuple<int, int>>() { { "default", new Tuple<int, int>(0, 0) } };
            var fileName = Settings.Default.GTAFolder + "\\update\\update.rpf";
            var fileInfo = new FileInfo(fileName);
            var fileStream = new FileStream(fileName, FileMode.Open);
            var inputArchive = RageArchiveWrapper7.Open(fileStream, fileInfo.Name);
            var doc = new XmlDocument();

            ArchiveUtilities.ForEachFile(fileName.Replace(Settings.Default.GTAFolder, ""), inputArchive.Root, inputArchive.archive_.Encryption, (string fullFileName, IArchiveFile file, RageArchiveEncryption7 encryption) =>
            {
                if (fullFileName.EndsWith("dlclist.xml"))
                {
                    byte[] data = Utils.GetBinaryFileData((IArchiveBinaryFile) file, encryption);
                    string xml;

                    if (data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)  // Detect BOM
                    {
                        xml = Encoding.UTF8.GetString(data, 3, data.Length - 3);
                    }
                    else
                    {
                        xml = Encoding.UTF8.GetString(data);
                    }

                    doc.LoadXml(xml);

                }

            });

            inputArchive.Dispose();


            ArchiveUtilities.ForEachFile(Settings.Default.GTAFolder, (string fullFileName, IArchiveFile file, RageArchiveEncryption7 encryption) =>
            {

                if (fullFileName.EndsWith("setup2.xml") && !fullFileName.StartsWith("\\mods"))
                {
                    byte[] data = Utils.GetBinaryFileData((IArchiveBinaryFile)file, encryption);
                    string xml;

                    if (data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)  // Detect BOM
                    {
                        xml = Encoding.UTF8.GetString(data, 3, data.Length - 3);
                    }
                    else
                    {
                        xml = Encoding.UTF8.GetString(data);
                    }

                    var matchOrder = orderRegex.Match(xml);
                    var matchMinOrder = minOrderRegex.Match(xml);
                    var matchPath = pathRegex.Match(fullFileName);
                    var matchPath2 = pathRegex2.Match(fullFileName);

                    var dlcName = matchPath.Success ? matchPath.Groups[1].Value : matchPath2.Groups[1].Value;

                    dlcOrders[dlcName] = new Tuple<int, int>(matchOrder.Success ? int.Parse(matchOrder.Groups[1].Value) : 0, matchMinOrder.Success ? int.Parse(matchMinOrder.Groups[1].Value) : 0);
                }
            });

            foreach (XmlNode pathsnode in doc.DocumentElement)
            {
                foreach (XmlNode itemnode in pathsnode.ChildNodes)
                {
                    string p      = itemnode.InnerText.ToLowerInvariant();
                    string[] path = p.Split('/');

                    if(path.Length - 2 < 0)
                    {
                        Console.Error.WriteLine("Ignoring " + p);
                    }
                    else
                    {
                        dlclist.Add(path[path.Length - 2]);
                    }
                }
            }

            var kvp = new List<KeyValuePair<string, Tuple<int, int>>>(); // dlc name => order, minOrder
            var list = new List<string>();

            foreach (var entry in dlcOrders)
                kvp.Add(entry);

            kvp.Sort((a, b) => {

                int test = a.Value.Item1 - b.Value.Item1;

                if (test == 0)
                    return dlclist.IndexOf(a.Key) - dlclist.IndexOf(b.Key);
                else
                    return test;
            });

            for (int i = 0; i < kvp.Count; i++)
                list.Add(kvp[i].Key);

            return list.ToArray();

        }

        public static int GetDLCLevel(string path)  // path must be a sub dir/file of the dlc
        {
            path = path.ToLowerInvariant().Replace('/', '\\');

            string[] split = path.Split('\\');

            if(split.Length == 1)
            {
                if(!DLCFiles.TryGetValue(path, out RpfFileEntry entry))
                {
                    return 0;
                }

                return entry.DlcLevel;
            }
            else
            {
                var pathRegex = new Regex(@"\\dlcpacks\\([a-z0-9_]*)\\");
                var pathRegex2 = new Regex(@"\\dlc_patch\\([a-z0-9_]*)\\");

                var match = pathRegex.Match(path);
                var match2 = pathRegex2.Match(path);

                if (match.Success)
                    return Array.IndexOf(DLCList, match.Groups[1].Value);
                else if (match2.Success)
                    return Array.IndexOf(DLCList, match2.Groups[1].Value);
                else
                    return 0;
            }

        }

        public static Drawable GetDrawable(uint hash)
        {
            Drawable drawable = null;

            if(!DrawableCache.ContainsKey(hash))
            {
                if(FileHashes.ContainsKey("ydr") && FileHashes["ydr"].ContainsKey(hash))
                {
                    FileHashes["ydr"].TryGetValue(hash, out RpfFileEntry rpfFileEntry);

                    Utils.ForFile(rpfFileEntry.FullFileName, (file, encryption) =>
                    {
                        DrawableCache[hash] = Utils.GetResourceData<Drawable>((IArchiveResourceFile)file);
                        drawable = DrawableCache[hash];
                    });
                }
            }
            else
            {
                drawable = DrawableCache[hash];
            }

            return drawable;
        }

        public static MCBaseArchetypeDef GetArchetype(uint hash)
        {
            ArchetypeCache.TryGetValue(hash, out MCBaseArchetypeDef archetype);
           
            return archetype;
        }
    }

    public struct RpfFileEntry
    {
        public uint Hash;
        public string FullFileName;
        public string Name;
        public int DlcLevel;
        public string Ext;

    }
}
