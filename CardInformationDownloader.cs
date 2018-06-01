using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
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
        private static readonly Regex _unitCostExpression = new Regex(@"(?:.\s)([0-9]{1,})", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

        internal static void UpdateCardUsingOCR(string filename, CardItem card)
        {
            using (var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default))
            using (var bitmap = RescaleBitmap(new Bitmap(filename), 2000, 2800))
            using (var pix = PixConverter.ToPix(bitmap))
            {
                if (card.Job.Equals("Unit", StringComparison.OrdinalIgnoreCase))
                {
                    string unitSubtitle = null;
                    using (var subtitle = engine.Process(pix, Rect.FromCoords(405, 205, 1750, 260), PageSegMode.SingleBlock))
                    {
                        unitSubtitle = subtitle.GetText();
                    }

                    if (string.IsNullOrWhiteSpace(unitSubtitle))
                    {
                        throw new NotSupportedException("Unit title could not be acquired by OCR.");
                    }

                    if (unitSubtitle.IndexOf("ATTACHMENT", StringComparison.OrdinalIgnoreCase) > -1)
                    {
                        card.UpdateAttachment();

                        using (var points = engine.Process(pix, Rect.FromCoords(1640, 2690, 1705, 2745), PageSegMode.SingleBlock))
                        {
                            UpdateCardInteger(card, card.UpdatePoints, points.GetText());
                        }
                    }
                    else
                    {
                        string unitCosts = null;
                        using (var costs = engine.Process(pix, Rect.FromCoords(1105, 2640, 1710, 2740), PageSegMode.SingleBlock))
                        {
                            unitCosts = costs.GetText();
                        }

                        if (string.IsNullOrWhiteSpace(unitCosts))
                        {
                            throw new NotSupportedException("Unit cost area could not be acquired by OCR.");
                        }

                        string[] lines = unitCosts.Split('\n');
                        var points = new List<string>();
                        var sizes = new List<string>();

                        for (int i = 0; i < lines.Length; i += 1)
                        {
                            if (string.IsNullOrWhiteSpace(lines[i]))
                            {
                                if (i == 0 || i == 1)
                                {
                                    continue;
                                }
                                else
                                {
                                    break;
                                }
                            }

                            // we're not whitespace, process the line with a poor regex
                            var matches = _unitCostExpression.Matches(lines[i]);
                            if (matches.Count == 1)
                            {
                                points.Add(matches[0].Groups[1].Value);
                            }
                            else if (matches.Count == 2)
                            {
                                sizes.Add(matches[0].Groups[1].Value);
                                points.Add(matches[1].Groups[1].Value);
                            }                            
                        }

                        if (points.Count == 1)
                        {
                            UpdateCardInteger(card, card.UpdatePoints, points[0]);
                        }
                        else if (points.Count == 2)
                        {
                            UpdateCardInteger(card, card.UpdatePointsMin, points[0]);
                            UpdateCardInteger(card, card.UpdatePointsMax, points[1]);
                        }

                        if (sizes.Count == 2)
                        {
                            UpdateCardInteger(card, card.UpdateSizeMin, sizes[0]);
                            UpdateCardInteger(card, card.UpdateSizeMax, sizes[1]);
                        }
                    }
                }
                else
                {
                    using (var points = engine.Process(pix, Rect.FromCoords(1620, 2690, 1725, 2745), PageSegMode.SingleBlock))
                    {
                        UpdateCardInteger(card, card.UpdatePoints, points.GetText());
                    }
                }

                using (var fieldAllowance = engine.Process(pix, Rect.FromCoords(1830, 2690, 1890, 2745), PageSegMode.SingleBlock))
                {
                    UpdateCardInteger(card, card.UpdateFieldAllowance, fieldAllowance.GetText(),
                        txt => txt.Equals("C", StringComparison.OrdinalIgnoreCase) ? 1 : -1,
                        txt => txt.Equals("U", StringComparison.OrdinalIgnoreCase) ? 999 : -1
                    );
                }
            }
        }

        private static Bitmap RescaleBitmap(Bitmap original, float width, float height)
        {
            Bitmap output = new Bitmap((int) width, (int) height);
            float scale = Math.Min(width / original.Width, height / original.Height);

            using (original)
            using (var gfx = Graphics.FromImage(output))
            {
                int scaleWidth = (int) (original.Width * scale);
                int scaleHeight = (int) (original.Height * scale);
                
                gfx.DrawImage(original, ((int) width - scaleWidth) / 2, ((int) height - scaleHeight) / 2, scaleWidth, scaleHeight);
            }

            return output;
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