using System;

namespace GTAUtil
{
    partial class Program
    {
        static void HandleFixDDSOptions(string[] args)
        {
            CommandLine.Parse<FIXDDSOptions>(args, (opts, gOpts) =>
            {
                foreach (var info in Utils.Expand(opts.InputFiles))
                {
                    if(info.Extension.EndsWith(".dds"))
                    {
                        Console.WriteLine(info.Name);
                    }
                }
            });
        }
    }
}
