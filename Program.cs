using System;
using System.IO;
using System.Threading.Tasks;

namespace wmcards
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Please provide an output path. Example: wmcards.exe c:\\output\\path");
                return;
            }

            if (args.Length > 1)
            {
                Console.WriteLine("Please provide only one (escaped) parameter; the output path. Example: wmcards.exe c:\\output\\path");
                return;
            }

            DirectoryInfo outputDirectory = new DirectoryInfo(args[0].Trim());

            if (!outputDirectory.Exists)
            {
                Console.WriteLine($"The provided output path does not exist: {outputDirectory.FullName}");
                return;
            }

            try
            {
                foreach (var file in outputDirectory.EnumerateFiles())
                {
                    file.Delete();
                }

                foreach (var dir in outputDirectory.EnumerateDirectories())
                {
                    dir.Delete(true);
                }
            }
            catch
            {
                Console.WriteLine($"The provided output path could not be written to: {outputDirectory.FullName}");
                Console.WriteLine("Please ensure you are using a directory to which your user has full access (such as your home directory).");
                return;
            }

            MainAsync(outputDirectory).GetAwaiter().GetResult();
        }

        static async Task MainAsync(DirectoryInfo outputDirectory)
        {
            using (var downloader = new CardInformationDownloader(new Uri("http://cards.privateerpress.com")))
            {
                await downloader.DownloadCardsTo(outputDirectory);
            }
        }
    }
}