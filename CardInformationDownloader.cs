using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Tesseract;
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
            _mutex = new SemaphoreSlim(3);
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

            var cardSummary = await GetCardSummary(_client, _uri);
            var cardsByFaction = cardSummary.Cards.GroupBy(c => c.Faction);

            var innerDirectories = outputDirectory.GetDirectories()
                .ToDictionary(d => d.Name);

            foreach (var factionCards in cardsByFaction)
            {
                var factionDirectory = innerDirectories.ContainsKey(factionCards.Key) ?
                    innerDirectories[factionCards.Key] :
                    outputDirectory.CreateSubdirectory(factionCards.Key);

                var factionDictionary = factionCards.ToDictionary(c => FormatFilename(c.Title, '-'));

                await DownloadFactionCards(factionDirectory, factionDictionary, _client, _uri, _mutex);
                await CreateFactionIndex(factionDirectory, cardSummary.FactionNames[factionCards.Key], factionCards);
            }
        }

        private static async Task CreateFactionIndex(DirectoryInfo factionDirectory, string factionName, IEnumerable<CardItem> factionCards)
        {
            if (factionDirectory == null)
            {
                throw new ArgumentNullException(nameof(factionDirectory));
            }

            if (!factionDirectory.Exists)
            {
                throw new ArgumentOutOfRangeException(nameof(factionDirectory), $"{nameof(factionDirectory)} does not exist.");
            }

            if (string.IsNullOrWhiteSpace(factionName))
            {
                throw new ArgumentNullException(nameof(factionName));
            }

            if (factionCards == null)
            {
                throw new ArgumentNullException(nameof(factionCards));
            }

            Stream fs = null;
            CardFaction faction = new CardFaction(factionName, factionCards);

            try
            {
                fs = new FileStream(Path.Combine(factionDirectory.FullName, "__cardindex.json"), FileMode.Create);
                await JsonSerializer.SerializeAsync(fs, faction, StandardResolver.ExcludeNullCamelCase);
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
                        await ProcessCard(factionDirectory, s, card.Key, card.Value);
                    })
                );

            }

            await Task.WhenAll(tasks);
        }

        private static async Task ProcessCard(DirectoryInfo factionDirectory, Stream stream, string cardKey, CardItem card)
        {
            var imageNames = new List<string>();

            using (stream)
            using (var document = PdfReader.Open(stream))
            {
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

                    string cardName = $"{cardKey}-{imageNames.Count + 1}.jpg";
                    string filename = Path.Combine(factionDirectory.FullName, cardName);

                    using (var outfs = new FileStream(filename, FileMode.Create))
                    {
                        await outfs.WriteAsync(xObject.Stream.Value, 0, xObject.Stream.Value.Length);
                        await outfs.FlushAsync();
                    }

                    if (imageNames.Count == 0)
                    {
                        UpdateCardUsingOCR(filename, card);
                    }

                    imageNames.Add(cardName);
                }
            }

            card.UpdateImageNames(imageNames);
        }

        private static void UpdateCardUsingOCR(string filename, CardItem card)
        {
            using (var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default))
            using (var pix = Pix.LoadFromFile(filename))
            {
                if (card.Job.Equals("Unit", StringComparison.OrdinalIgnoreCase))
                {
                    using (var points6 = engine.Process(pix, Rect.FromCoords(614, 987, 642, 1010), PageSegMode.SingleWord))
                    {
                        UpdateCardInteger(card, card.UpdatePoints6, points6.GetText());
                    }

                    using (var points10 = engine.Process(pix, Rect.FromCoords(614, 1007, 642, 1032), PageSegMode.SingleWord))
                    {
                        UpdateCardInteger(card, card.UpdatePoints10, points10.GetText());
                    }

                    using (var min = engine.Process(pix, Rect.FromCoords(485, 989, 500, 1008), PageSegMode.SingleChar))
                    {
                        UpdateCardInteger(card, card.UpdateSizeMinimum, min.GetText());
                    }

                    using (var max = engine.Process(pix, Rect.FromCoords(485, 1007, 500, 1027), PageSegMode.SingleChar))
                    {
                        UpdateCardInteger(card, card.UpdateSizeMaximum, max.GetText());
                    }
                }
                else
                {
                    using (var points = engine.Process(pix, Rect.FromCoords(608, 1010, 645, 1030), PageSegMode.SingleWord))
                    {
                        UpdateCardInteger(card, card.UpdatePoints, points.GetText());
                    }
                }

                if (card.Job.Equals("Warcaster", StringComparison.OrdinalIgnoreCase))
                {
                    using (var focus = engine.Process(pix, Rect.FromCoords(180, 635, 215, 690), PageSegMode.SingleWord))
                    {
                        UpdateCardInteger(card, card.UpdateFocus, focus.GetText());
                    }
                }

                if (card.Job.Equals("Warlock", StringComparison.OrdinalIgnoreCase))
                {
                    using (var focus = engine.Process(pix, Rect.FromCoords(180, 640, 220, 685), PageSegMode.SingleWord))
                    {
                        UpdateCardInteger(card, card.UpdateFury, focus.GetText());
                    }
                }

                using (var fieldAllowance = engine.Process(pix, Rect.FromCoords(690, 1010, 710, 1030), PageSegMode.SingleChar))
                {
                    UpdateCardInteger(card, card.UpdateFieldAllowance, fieldAllowance.GetText(), 
                        txt => txt.Equals("C", StringComparison.OrdinalIgnoreCase) ? 1 : -1,
                        txt => txt.Equals("U", StringComparison.OrdinalIgnoreCase) ? 999 : -1
                    );
                }
            }
        }

        private static void UpdateCardInteger(CardItem card, Action<int> method, string text, params Func<string, int>[] tests)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string formatted = text.Trim();

            foreach(var test in tests)
            {
                int result = test(formatted);
                if (result > 0)
                {
                    method(result);
                    return;
                }
            }

            int tmp;
            if (int.TryParse(formatted, out tmp))
            {
                method(tmp);
            }
        }

        private static async Task<CardSummary> GetCardSummary(HttpClient client, Uri uri)
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
            IReadOnlyDictionary<string, string> factions = null;
            HtmlDocument doc = null;

            try
            {
                stream = await client.GetStreamAsync(uri);
                doc = new HtmlDocument();
                doc.Load(stream);
                cards = ProcessCardHtml(doc);
                factions = ProcessFactionHtml(doc);
            }
            catch
            {
                Console.WriteLine($"An error occured downloading information from: {uri}");
                throw;
            }
            finally
            {
                if (doc != null)
                {
                    doc = null;
                }

                if (stream != null)
                {
                    stream.Dispose();
                    stream = null;
                }
            }

            return new CardSummary(cards, factions);
        }

        private static IReadOnlyList<CardItem> ProcessCardHtml(HtmlDocument doc)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

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

        private static IReadOnlyDictionary<string, string> ProcessFactionHtml(HtmlDocument doc)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            var factionNode = doc.DocumentNode.SelectSingleNode("//div[@id='general-faction-tabs']");
            var tabNodes = factionNode.SelectNodes("//a[contains(@class, 'mdl-tabs__tab')]");
            var tabs = new Dictionary<string, string>();

            foreach (var node in tabNodes)
            {
                var href = node.GetAttributeValue("href", string.Empty);
                var title = node.GetAttributeValue("title", string.Empty);

                if (string.IsNullOrEmpty(href) || string.IsNullOrEmpty(title) || !href.StartsWith("#"))
                {
                    continue;
                }

                tabs.Add(href.Substring(1), title);
            }

            return tabs;
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