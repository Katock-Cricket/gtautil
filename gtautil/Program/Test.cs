﻿
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using RageLib.Archives;
using RageLib.GTA5.Archives;
using RageLib.GTA5.Utilities;
using RageLib.Resources.GTA5.PC.GameFiles;

namespace GTAUtil
{
    partial class Program
    {
        static void HandleTestOptions(string[] args)
        {
            CommandLine.Parse<TestOptions>(args, (opts, gOpts) =>
            {
                //Init(args);
            });
        }
    }
}
