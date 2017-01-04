using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using HtmlAgilityPack;
using Pretzel.Logic.Extensibility;
using Pretzel.Logic.Templating.Context;
using System;
using LibSassHost;
using LibSassHost.Helpers;

namespace Pretzel.Logic.Minification
{
    public class SassTransform : ITransform
    {
        private static readonly string[] ExternalProtocols = { "http", "https", "//" };

#pragma warning disable 0649
        private readonly IFileSystem fileSystem;
#pragma warning restore 0649

        [ImportingConstructor]
        public SassTransform(IFileSystem fileSystem)
        {
            this.fileSystem = fileSystem;
            Console.WriteLine("created SassTransform");
        }

        private string filePath = string.Empty;        

        public void Transform(SiteContext siteContext)
        {
            var shouldCompile = new List<Page>();
            //Process to see if the site has a CSS file that doesn't exist, and should be created from LESS files.
            //This is "smarter" than just compiling all Less files, which will crash if they're part of a larger Less project 
            //ie, one file pulls in imports, individually they don't know about each other but use variables
            foreach (var file in siteContext.Pages.Where(p => p.OutputFile.EndsWith(".html") && fileSystem.File.Exists(p.OutputFile)))
            {
                var doc = new HtmlDocument();
                var fileContents = fileSystem.File.ReadAllText(file.OutputFile);
                doc.LoadHtml(fileContents);

                var nodes = doc.DocumentNode.SelectNodes("/html/head/link[@rel='stylesheet']");
                if (nodes != null)
                    foreach (HtmlNode link in nodes)
                    {
                        var cssfile = link.Attributes["href"].Value;

                        // If the file is not local, ignore it
                        var matchingIgnoreProtocol = ExternalProtocols.FirstOrDefault(cssfile.StartsWith);
                        if (matchingIgnoreProtocol != null)
                            continue;

                        //If the file exists, ignore it
                        if (fileSystem.File.Exists(Path.Combine(siteContext.OutputFolder, cssfile)))
                            continue;

                        //If there is a CSS file that matches the name, ignore, could be another issue
                        if (siteContext.Pages.Any(p => p.OutputFile.Contains(cssfile)))
                            continue;


                        var n = cssfile.Replace(".css", ".scss");
                        n = n.Replace('/', '\\');

                        var cssPageToCompile = siteContext.Pages.FirstOrDefault(f => f.OutputFile.Contains(n));
                        if (cssPageToCompile != null && !shouldCompile.Contains(cssPageToCompile))
                        {
                            shouldCompile.Add(cssPageToCompile);
                        }
                    }
            }

            foreach (var less in shouldCompile)
            {
                filePath = less.OutputFile;
                var outputfile = less.OutputFile.Replace(".scss", ".css");
                var inputContent = fileSystem.File.ReadAllText(less.OutputFile);
                fileSystem.File.WriteAllText(outputfile, ProcessCss(siteContext, inputContent, less.File, outputfile));
                fileSystem.File.Delete(less.OutputFile);
            }
        }

        public string ProcessCss(SiteContext siteContext, string inputContent, string inputFile, string outputFile)
        {                      
            using (var compiler = new SassCompiler())
            {
                try
                {
                    var options = new CompilationOptions { SourceMap = true };
                    CompilationResult result = compiler.Compile(inputContent, inputFile, outputFile, options);

                    Console.WriteLine("Compiled content:{1}{1}{0}{1}", result.CompiledContent,
                        Environment.NewLine);
                    Console.WriteLine("Source map:{1}{1}{0}{1}", result.SourceMap, Environment.NewLine);
                    Console.WriteLine("Included file paths: {0}",
                        string.Join(", ", result.IncludedFilePaths));

                    return result.CompiledContent;
                }
                catch (SassСompilationException e)
                {
                    Console.WriteLine("During compilation of SCSS code an error occurred. See details:");
                    Console.WriteLine();
                    Console.WriteLine(SassErrorHelpers.Format(e));
                    return string.Empty;
                }
            }
            
            
        }
    }
}
