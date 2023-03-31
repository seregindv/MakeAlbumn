using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using System.Globalization;
using System.Text;

namespace MakeAlbumn
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            var cmdLine = new CommandLine(args);
            var useClipboard = cmdLine.IsSwitchOn("clp");
            if (cmdLine.ParameterCount < 1 || (!useClipboard && cmdLine.ParameterCount < 2))
            {
                PrintUsage();
                return;
            }
            if (!useClipboard && !Directory.Exists(cmdLine.Parameter(0)))
            {
                Console.WriteLine("Directory {0} doesn't exist", cmdLine.Parameter(0));
                return;
            }
            var targetFile = cmdLine.Parameter(useClipboard ? 0 : 1);
            var targetFileDir = Path.GetDirectoryName(targetFile);
            if (!String.IsNullOrEmpty(targetFileDir) && !Directory.Exists(targetFileDir))
            {
                Console.WriteLine("Directory for {0} doesn't exist", targetFile);
                return;
            }
            var maker = new PdfMaker();
            maker.Make(cmdLine);
            if (cmdLine.IsSwitchOn("wait"))
            {
                Console.WriteLine("All done. Press enter");
                Console.Read();
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage: {0} <<dir>|/clp> <pdf> [/wait] [/mapfile:<file>] [/perdocument:<photocount>] [</noparallel>|</degree:<degree of parallelism>>] [/orderbysize] [/outputxml:<filename>]", Assembly.GetExecutingAssembly().ManifestModule.Name);
        }
    }
}
