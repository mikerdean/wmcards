using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Utf8Json;
using Utf8Json.Resolvers;

namespace wmcards
{
    class CardInformationDownloader : IDisposable
    {
        private const string _allowedFilenameCharacters = "abcdefghijklmnopqrstuvwxyz1234567890ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private HttpClient _client;
        private bool _disposed;
        private SemaphoreSlim _mutex;
        private Uri _uri;

        public CardInformationDownloader(Uri uri)
        {
            if (uri == null)
            {
                throw new ArgumentNullException(nameof(uri));
            }

            if (!uri.IsAbsoluteUri)
            {
                throw new ArgumentOutOfRangeException(nameof(uri), $"{nameof(uri)} must be absolute.");
            }

            _client = new HttpClient();
            _mutex = new SemaphoreSlim(5);
            _uri = uri;
        }

        public async Task DownloadCardsTo(DirectoryInfo outputDirectory)
        {
            if (outputDirectory == null)
            {
                throw new ArgumentNullException(nameof(outputDirectory));
            }

            if (!outputDirectory.Exists)
            {
                throw new ArgumentOutOfRangeException(nameof(outputDirectory), $"{nameof(outputDirectory)} does not exist.");
            }

            var cardList = await GetCardList(_client, _uri);
            var cardsByFaction = cardList.GroupBy(c => c.Faction);

            foreach (var factionCards in cardsByFaction)
            {
                string folderName = FormatFilename(factionCards.Key, '-');
                DirectoryInfo factionDirectory = outputDirectory.CreateSubdirectory(folderName);
                Dictionary<string, CardItem> factionDictionary = factionCards.ToDictionary(c => FormatFilename(c.Title, '-'));

                await CreateFactionIndex(factionDirectory, factionDictionary);
                await DownloadFactionCards(factionDirectory, factionDictionary, _client, _uri, _mutex);
            }
        }

        private static async Task CreateFactionIndex(DirectoryInfo factionDirectory, Dictionary<string, CardItem> factionDictionary)
        {
            if (factionDirectory == null)
            {
                throw new ArgumentNullException(nameof(factionDirectory));
            }

            if (!factionDirectory.Exists)
            {
                throw new ArgumentOutOfRangeException(nameof(factionDirectory), $"{nameof(factionDirectory)} does not exist.");
            }

            if (factionDictionary == null)
            {
                throw new ArgumentNullException(nameof(factionDictionary));
            }

            Stream fs = null;

            try
            {
                fs = new FileStream(Path.Combine(factionDirectory.FullName, "__cardindex.json"), FileMode.Create);
                await JsonSerializer.SerializeAsync(fs, factionDictionary, StandardResolver.CamelCase);
                await fs.FlushAsync();
            }
            catch
            {
                Console.WriteLine($"An error occured writing faction card index information to: {factionDirectory.FullName}");
                throw;
            }
            finally
            {
                if (fs != null)
                {
                    fs.Dispose();
                    fs = null;
                }
            }
        }

        private static async Task DownloadFactionCards(DirectoryInfo factionDirectory, Dictionary<string, CardItem> factionDictionary, HttpClient client, Uri baseUri, SemaphoreSlim mutex)
        {
            if (factionDirectory == null)
            {
                throw new ArgumentNullException(nameof(factionDirectory));
            }

            if (!factionDirectory.Exists)
            {
                throw new ArgumentOutOfRangeException(nameof(factionDirectory), $"{nameof(factionDirectory)} does not exist.");
            }

            if (factionDictionary == null)
            {
                throw new ArgumentNullException(nameof(factionDictionary));
            }

            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (baseUri == null || !baseUri.IsAbsoluteUri)
            {
                throw new ArgumentOutOfRangeException(nameof(baseUri), $"Please provide a valid {nameof(baseUri)}.");
            }

            if (mutex == null)
            {
                throw new ArgumentNullException(nameof(mutex));
            }

            /* 
             * http://cards.privateerpress.com?card_items_to_pdf=${0},{1} 
             * {0} <- card id
             * {1} <- card quantity (in our case will always be one)
             */

            var tasks = new List<Task>();

            foreach (var card in factionDictionary)
            {
                Console.WriteLine($"Acquiring {card.Value.Title}");

                await mutex.WaitAsync();

                Uri uri = new Uri(baseUri, $"?card_items_to_pdf=${card.Value.Id},1");

                tasks.Add(
                    client.GetAsync(uri).ContinueWith(async t => {
                        mutex.Release();
                        Stream s = await t.Result.Content.ReadAsStreamAsync();
                        await ProcessCard(factionDirectory, s, card.Key);
                    })
                );

            }

            await Task.WhenAll(tasks);
        }

        private static async Task ProcessCard(DirectoryInfo factionDirectory, Stream stream, string cardKey)
        {
            using (stream)
            using (var document = PdfReader.Open(stream))
            {
                int imageCount = 0;

                PdfDictionary resources = document.Pages.Elements.GetDictionary("/Resources");
                PdfDictionary xObjects = resources.Elements.GetDictionary("/XObject");

                foreach (var item in xObjects.Elements.Values)
                {
                    var reference = item as PdfReference;
                    if (reference == null)
                    {
                        continue;
                    }

                    var xObject = reference.Value as PdfDictionary;
                    if (xObject == null || xObject.Elements.GetString("/Subtype") != "/Image")
                    {
                        continue;
                    }

                    if (xObject.Elements.GetInteger("/Height") != 1050 || xObject.Elements.GetInteger("/Width") != 750)
                    {
                        continue;
                    }

                    imageCount += 1;

                    using (var outfs = new FileStream(Path.Combine(factionDirectory.FullName, $"{cardKey}-{imageCount}.jpg"), FileMode.Create))
                    {
                        await outfs.WriteAsync(xObject.Stream.Value, 0, xObject.Stream.Value.Length);
                        await outfs.FlushAsync();
                    }
                }
            }
        }

        private static async Task<IReadOnlyList<CardItem>> GetCardList(HttpClient client, Uri uri)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (uri == null || !uri.IsAbsoluteUri)
            {
                throw new ArgumentOutOfRangeException(nameof(uri), $"Please provide a valid {nameof(uri)}.");
            }

            Stream stream = null;
            IReadOnlyList<CardItem> cards = null;

            try
            {
                stream = await client.GetStreamAsync(uri);
                cards = ProcessCardHtml(stream);
            }
            catch
            {
                Console.WriteLine($"An error occured downloading information from: {uri}");
                throw;
            }
            finally
            {
                if (stream != null)
                {
                    stream.Dispose();
                    stream = null;
                }
            }

            return cards;
        }

        private static IReadOnlyList<CardItem> ProcessCardHtml(Stream stream)
        {
            var doc = new HtmlDocument();
            doc.Load(stream);

            var cardNodes = doc.DocumentNode.SelectNodes("//carditem");
            var cards = new List<CardItem>();

            foreach(var node in cardNodes)
            {
                cards.Add(new CardItem(
                    node.GetAttributeValue(":card", 0),
                    node.GetAttributeValue("faction", "unknown"),
                    node.GetAttributeValue("job", "job"),
                    node.GetAttributeValue("title", "unknown")
                ));
            }

            return cards;
        }

        private static string FormatFilename(string value, char separator)
        {
            StringBuilder sb = new StringBuilder();
            bool wasControlCharacter = false;

            for (int i = 0; i < value.Length; i += 1)
            {
                if (char.IsWhiteSpace(value, i))
                {
                    if (!wasControlCharacter)
                    {
                        sb.Append(separator);
                        wasControlCharacter = true;
                    }
                }
                else if (value[i] == separator)
                {
                    if (!wasControlCharacter)
                    {
                        sb.Append(separator);
                        wasControlCharacter = true;
                    }
                }
                else if (_allowedFilenameCharacters.IndexOf(value[i]) > -1)
                {
                    sb.Append(char.ToLower(value[i]));
                    wasControlCharacter = false;
                }
            }

            return sb.ToString();
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_client != null)
                    {
                        _client.Dispose();
                    }

                    if (_mutex != null)
                    {
                        _mutex.Dispose();
                    }

                    _client = null;
                    _mutex = null;
                    _uri = null;
                }

                _disposed = true;
            }
        }
    }
}