using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Drawing;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using Fonet;
using Fonet.Render.Pdf;
using System.Globalization;

namespace MakeAlbumn
{
    class Program
    {
        const string FO_NAMESPACE = "http://www.w3.org/1999/XSL/Format";

        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: {0} <dir> <pdf>", Assembly.GetExecutingAssembly().ManifestModule.Name);
                return;
            }
            var dir = args[0];
            var targetFile = args[1];
            MakePdf(Directory.EnumerateFiles(dir, "*.jpg"), targetFile);
        }

        static void MakePdf(IEnumerable<string> jpegFiles, string targetFile)
        {
            Console.WriteLine("Determining files dimensions...");
            var files = jpegFiles
                .AsParallel()
                .Select((file, fileNum) =>
                {
                    using (var image = Image.FromFile(file))
                    {
                        return new { image.Size, FileName = file };
                    }
                })
                .OrderBy(file => file.FileName)
                .ToArray();
            Console.WriteLine("Creating XML...");
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
            var pageTemplates = sizeDic
                .Select(dicElement =>
                    new XElement(GetName("simple-page-master"),
                        new XAttribute("master-name", dicElement.Value.Id),
                        new XAttribute("page-width", dicElement.Value.Size.Width),
                        new XAttribute("page-height", dicElement.Value.Size.Height),
                        new XElement(GetName("region-body"))
                        )
                    );
            var pages = files
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
                new XElement(GetName("layout-master-set"), pageTemplates),
                pages));

            using (var memoryStream = new MemoryStream())
            {
                using (var xmlWriter = XmlWriter.Create(memoryStream))
                    doc.WriteTo(xmlWriter);
                memoryStream.Seek(0, SeekOrigin.Begin);
                var fonet = FonetDriver.Make();
                fonet.Options = new PdfRendererOptions { FontType = FontType.Subset };
                Console.WriteLine("Writing PDF...");
                using (var fileStream = File.Create(targetFile))
                    fonet.Render(memoryStream, fileStream);
            }
        }

        static XName GetName(string tag)
        {
            return XName.Get(tag, FO_NAMESPACE);
        }
    }
}
