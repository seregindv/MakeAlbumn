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
using Fonet;
using Fonet.Image;
using Fonet.Render.Pdf;
using System.Globalization;
using System.Text;

namespace MakeAlbumn
{
    static class Program
    {
        const string FO_NAMESPACE = "http://www.w3.org/1999/XSL/Format";

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
            MakePdfs(GetFiles(cmdLine), cmdLine);
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

        static void MakePdfs(IEnumerable<string> jpegFiles, CommandLine cmdLine)
        {
            Console.WriteLine("Determining files dimensions...");
            var files = jpegFiles
                .AsParallel()
                .Select((file, fileNum) =>
                {
                    try
                    {
                        using (var fs = new FileStream(file, FileMode.Open))
                        {
                            var jpi = new JpegParser(fs).Parse();
                            return new { Size = new ComparableSize(jpi.Width, jpi.Height), FileName = file, FileNum = fileNum };
                        }
                    }
                    catch
                    {
                        using (var image = Image.FromFile(file))
                        {
                            return new { Size = new ComparableSize(image.Size), FileName = file, FileNum = fileNum };
                        }
                    }
                })
                .ToArray();
            var sizeDic = files
                .Select(file => file.Size)
                .Distinct()
                .Select((size, i) => new { Size = size, Id = "p" + (i + 1) })
                .ToDictionary(
                    superSize => superSize.Size,
                    superSize => new
                    {
                        Size = new
                        {
                            Width = (superSize.Size.Width / 300D).ToString("#.0000in", CultureInfo.InvariantCulture),
                            Height = (superSize.Size.Height / 300D).ToString("#.0000in", CultureInfo.InvariantCulture)
                        },
                        superSize.Id
                    });
            Func<string, string, string, XElement> getPageTemplate = (id, width, height) => new XElement(GetName("simple-page-master"),
                        new XAttribute("master-name", id),
                        new XAttribute("page-width", width),
                        new XAttribute("page-height", height),
                        new XElement(GetName("region-body"))
                        );
            var pageTemplates = sizeDic
                .Select(dicElement =>
                    getPageTemplate(dicElement.Value.Id, dicElement.Value.Size.Width, dicElement.Value.Size.Height)
                    );
            Dictionary<int, City> map = null;
            Regex fileNumEx = null;
            var mapFile = cmdLine.Switch("mapfile", File.Exists);
            var maxPhotocountPerDocument = cmdLine.IntSwitch("perdocument", Int32.MaxValue, i => i > 0);
            if (mapFile != null)
            {
                map = GetTownMap(mapFile);
                fileNumEx = new Regex(@"\d+");
            }
            var grouped = mapFile != null ? files.GroupBy(file =>
                {
                    var matches = fileNumEx.Matches(Path.GetFileNameWithoutExtension(file.FileName));
                    if (matches.Count > 0)
                    {
                        var id = map.Where(pair => pair.Key <= Int32.Parse(matches[0].Value)).Max(pair => pair.Key);
                        map[id].PhotoCount++;
                        var result = map[id].Name;
                        var page = map[id].PhotoCount / maxPhotocountPerDocument;
                        if (page > 0)
                            result += page;
                        return result;
                    }
                    return "Unknown";
                })
                : files.GroupBy(file => (file.FileNum / maxPhotocountPerDocument + 1).ToString());
            var parallel = grouped.AsParallel();
            if (cmdLine.IsSwitchOn("noparallel"))
                parallel = parallel.WithDegreeOfParallelism(1);
            else
            {
                var degree = cmdLine.IntSwitch("degree", 0);
                if (degree > 0)
                    parallel = parallel.WithDegreeOfParallelism(degree);
            }
            var outFileIndex = cmdLine.IsSwitchOn("clp") ? 0 : 1;
            var orderBySize = cmdLine.IsSwitchOn("orderbysize");
            parallel.ForAll(groupedFiles =>
                {
                    var title = (mapFile == null ? Path.GetFileNameWithoutExtension(cmdLine.Parameter(outFileIndex)) : String.Empty);
                    if (grouped.Count() > 1)
                        title += groupedFiles.Key;
                    var fileName = Path.Combine(Path.GetDirectoryName(cmdLine.Parameter(outFileIndex))
                        , title + Path.GetExtension(cmdLine.Parameter(outFileIndex)));
                    Console.WriteLine("Creating {0}...", fileName);
                    try
                    {
                        var pages = groupedFiles
                            .OrderBy(file => orderBySize ? (IComparable)file.Size : (IComparable)file.FileName)
                            .Select(file =>
                            new XElement(GetName("page-sequence"),
                                new XAttribute("master-reference", sizeDic[file.Size].Id),
                                new XElement(GetName("flow"),
                                    new XAttribute("flow-name", "xsl-region-body"),
                                    new XElement(GetName("external-graphic"),
                                        new XAttribute("src", "url('" + file.FileName + "')"),
                                        new XAttribute("content-height", sizeDic[file.Size].Size.Height),
                                        new XAttribute("content-width", sizeDic[file.Size].Size.Width)))));
                        var doc = new XDocument(
                           new XElement(GetName("root"),
                           new XAttribute("xmlns", FO_NAMESPACE),
                           new XElement(GetName("layout-master-set"), getPageTemplate("p0", "60mm", "84mm"), pageTemplates),
                           GetCoverPage(title), pages));
                        using (var memoryStream = CreateStream(cmdLine))
                        {
                            using (var xmlWriter = XmlWriter.Create(memoryStream))
                                doc.WriteTo(xmlWriter);
                            memoryStream.Seek(0, SeekOrigin.Begin);
                            var fonet = FonetDriver.Make();
                            FonetDriver.FonetEventHandler handler = (o, e) => { };
                            fonet.OnInfo += handler;
                            fonet.OnWarning += handler;
                            fonet.Options = new PdfRendererOptions { FontType = FontType.Subset };
                            using (var fileStream = File.Create(fileName))
                                fonet.Render(memoryStream, fileStream);
                            GC.Collect(); // i know, but fo.net
                        }
                        Console.WriteLine("{0} created", fileName);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error creating {0}\n{1}", fileName, ex);
                        File.Delete(fileName);
                    }
                });
        }

        private static Dictionary<int, City> GetTownMap(string mapFile)
        {
            var regex = new Regex(@"^(\d+)\s+(.*)");
            return (from line in File.ReadAllLines(mapFile, Encoding.Default)
                    let match = regex.Match(line)
                    where match.Success
                    select new { Num = Int32.Parse(match.Groups[1].Value), City = TrimIllegalChars(match.Groups[2].Value) })
                .ToDictionary(town => town.Num, town => new City(town.City, 0));
        }

        static XName GetName(string tag)
        {
            return XName.Get(tag, FO_NAMESPACE);
        }

        static string TrimIllegalChars(string path)
        {
            return String.Concat(path.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        }

        static IEnumerable<string> GetFiles(CommandLine cmdLine)
        {
            if (!cmdLine.IsSwitchOn("clp"))
                return Directory.EnumerateFiles(cmdLine.Parameter(0), "*.jpg");
            else
            {
                var clipData = Clipboard.GetText();
                return clipData.Split(Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            }
        }

        static XElement GetCoverPage(string title)
        {
            return
                new XElement(GetName("page-sequence"),
                    new XAttribute("master-reference", "p0"),
                    new XElement(GetName("flow"),
                        new XAttribute("flow-name", "xsl-region-body"),
                        new XAttribute("font-family", "Tahoma"),
                        new XElement(GetName("table"),
                            new XAttribute("width", "60mm"),
                            new XAttribute("height", "84mm"),
                            new XAttribute("table-layout", "fixed"),
                            new XElement(GetName("table-column")),
                            new XElement(GetName("table-body"),
                                new XElement(GetName("table-row"),
                                    new XAttribute("height", "25mm"),
                                    new XElement(GetName("table-cell"),
                                        new XElement(GetName("block")))),
                                new XElement(GetName("table-row"),
                                    new XAttribute("height", "84mm"),
                                    new XElement(GetName("table-cell"),
                                        new XAttribute("text-align", "center"),
                                        new XAttribute("vertical-align", "middle"),
                                        new XElement(GetName("block"),
                                            new XAttribute("font-size", "20pt"),
                                            title)))))));
        }

        static Stream CreateStream(CommandLine cmdLine)
        {
            var xmlFile = cmdLine.Switch("outputxml");
            if (xmlFile != null)
            {
                return new FileStream(xmlFile, FileMode.Create);
            }
            return new MemoryStream();
        }
    }

    class City
    {
        public City(string name, int photoCount)
        {
            Name = name;
            PhotoCount = photoCount;
        }

        public string Name { set; get; }
        public int PhotoCount { set; get; }
    }

    internal struct ComparableSize : IComparable<ComparableSize>, IComparable
    {
        public int Width { set; get; }
        public int Height { set; get; }

        public ComparableSize(int width, int height)
            : this()
        {
            Width = width;
            Height = height;
        }

        public ComparableSize(Size size)
            : this()
        {
            Width = size.Width;
            Height = size.Height;
        }

        #region Implementation of IComparable<in ComparableSize>

        public int CompareTo(ComparableSize other)
        {
            return ToInt().CompareTo(other.ToInt());
        }

        #endregion

        #region Implementation of IComparable

        public int CompareTo(object obj)
        {
            if (!(obj is ComparableSize))
                return 1;
            return CompareTo((ComparableSize)obj);
        }

        #endregion

        private int ToInt()
        {
            return Width * 100000 + Height;
        }
    }
}
