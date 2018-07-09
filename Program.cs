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

            string tmpPath = Path.Combine(outputDirectory.FullName, "__wmcards.tmp");

            try
            {
                using (var fs = new FileStream(tmpPath, FileMode.Create))
                {
                    fs.WriteByte(0xff);
                }
            }
            catch
            {
                Console.WriteLine($"You do not have write permissions to the output path provided: {outputDirectory.FullName}");
                return;
            }
            finally
            {
                if (File.Exists(tmpPath))
                {
                    File.Delete(tmpPath);
                }
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